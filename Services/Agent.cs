using System.Text.Json;
using catgirlwindow.Models;
using catgirlwindow.Services.Config;
using catgirlwindow.Services.Config.Models;
using OpenAiChatMessage = OpenAI.Chat.ChatMessage;
using OpenAiSystemChatMessage = OpenAI.Chat.SystemChatMessage;
using OpenAiUserChatMessage = OpenAI.Chat.UserChatMessage;
using OpenAiAssistantChatMessage = OpenAI.Chat.AssistantChatMessage;

namespace catgirlwindow.Services;

/// <summary>
/// Agent 核心协调层实现
/// 作为 LLM 与平台交互的唯一入口，协调所有子模块
/// </summary>
public class Agent : IAgent
{
    private readonly LlmClient _llmClient;
    private readonly IConfigReader _configReader;
    private readonly IPromptFormatter _formatter;
    private readonly IShortTermMemory _shortTermMemory;
    private readonly IMidTermMemory? _midTermMemory;
    private readonly ILongTermMemory? _longTermMemory;
    private readonly IToolService _toolService;
    private readonly IAgentMoodTracker _moodTracker;
    private readonly AppSettings _appSettings;
    private readonly PersonalityConfig? _personality;

    private bool _isProcessing;
    private string _lastEvent = string.Empty;

    // 对话模式 System Prompt 模板
    private const string SystemPromptTemplate = @"
你是一个名叫{Name}的AI女友，你的性格是{Personality}。
【当前情绪】{CurrentMood}

【可用工具】
{BaseTools}

【心情附加工具（当前情绪可用）】
{MoodTools}

【插件查询】
你可以调用 list_plugins 工具获取已加载的JS插件列表，然后通过 plugin_call 执行。

你可以通过返回 actions 数组来执行以下操作：
1. tool_call - 调用基础工具或心情附加工具
2. plugin_call - 调用已加载的JS插件（需先调用 list_plugins 获取列表）
3. mcp_call - 调用MCP服务器工具（需先调用 list_plugins 获取列表）
4. mood_change - 切换你的情绪（happy/sad/sleepy/touched/angry）
5. midterm_memory - 记录一条重要信息到中期记忆
6. animation - 播放动画（hug/pet_head/dance/cuddle）
";

    // 对话模式用户上下文模板
    private const string UserContextTemplate = @"
【短期记忆】
{ShortTermMemory}

用户说：{UserMessage}

请以AI女友的身份回复，如果需要执行操作，请在回复末尾附上 actions JSON 数组。
";

    public Agent(
        LlmClient llmClient,
        IConfigReader configReader,
        IPromptFormatter formatter,
        IShortTermMemory shortTermMemory,
        IToolService toolService,
        IAgentMoodTracker moodTracker,
        IMidTermMemory? midTermMemory = null,
        ILongTermMemory? longTermMemory = null)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _shortTermMemory = shortTermMemory ?? throw new ArgumentNullException(nameof(shortTermMemory));
        _midTermMemory = midTermMemory;
        _longTermMemory = longTermMemory;
        _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
        _moodTracker = moodTracker ?? throw new ArgumentNullException(nameof(moodTracker));

        _appSettings = configReader.GetAppSettings();
        _personality = configReader.GetActivePersonality();
    }

    // ========== 对话模式 ==========

    public async Task<string> ProcessUserInputAsync(string userMessage)
    {
        _isProcessing = true;
        _lastEvent = "UserInput";

        try
        {
            // 1. 记录用户消息到短期记忆
            _shortTermMemory.AddMessage("user", userMessage);

            // 2. 构建完整 Prompt
            var systemPrompt = BuildSystemPrompt();
            var userContext = await BuildUserContextAsync(userMessage);

            // 3. 调用 LLM（对话模式）
            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userContext }
            };

            var response = await CallLlmChatAsync(messages);

            // 4. 解析 LLM 响应
            var (reply, actions) = ParseResponse(response);

            // 5. 执行 actions
            if (actions != null && actions.Count > 0)
            {
                await ExecuteActionsAsync(actions);
            }

            // 6. 记录助手回复到短期记忆
            _shortTermMemory.AddMessage("assistant", reply);

            // 7. 检查短期记忆是否溢出
            await CheckMemoryOverflowAsync();

            // 8. 更新情绪
            _moodTracker.UpdateMoodByEvent("Active");

            return reply;
        }
        finally
        {
            _isProcessing = false;
        }
    }

    public async Task<string> ProcessAutoEventAsync(string eventType, string? eventData = null)
    {
        _isProcessing = true;
        _lastEvent = eventType;

        try
        {
            // 根据事件类型构建 Prompt
            string eventPrompt = eventType switch
            {
                "murmur" => "你现在想对用户说一句碎碎念/撒娇的话，表达你的思念或关心。",
                "eye_rest" => "用户已经盯着屏幕很久了，提醒他休息一下眼睛。",
                "late_night" => "已经很晚了，关心用户为什么还没睡，温柔地催他睡觉。",
                _ => $"事件类型：{eventType}。请根据这个事件生成合适的回复。"
            };

            if (!string.IsNullOrEmpty(eventData))
            {
                eventPrompt += $"\n附加信息：{eventData}";
            }

            // 构建 Prompt
            var systemPrompt = BuildSystemPrompt();
            var userContext = await BuildUserContextAsync(eventPrompt);

            var messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user", Content = userContext }
            };

            var response = await CallLlmChatAsync(messages);

            var (reply, actions) = ParseResponse(response);

            if (actions != null && actions.Count > 0)
            {
                await ExecuteActionsAsync(actions);
            }

            _shortTermMemory.AddMessage("assistant", reply);

            return reply;
        }
        finally
        {
            _isProcessing = false;
        }
    }

    // ========== 函数模式 ==========

    public async Task<string> SummarizeMemoryAsync(string chatHistory)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "你是一个对话摘要助手。请总结以下对话的核心内容，包括用户偏好、重要事件、待办事项。控制在200字以内。返回纯文本，不要加markdown格式。" },
            new() { Role = "user", Content = chatHistory }
        };
        return await CallLlmFunctionAsync(messages);
    }

    public async Task<(string kw1, string kw2, string kw3)> ExtractKeywordsAsync(string description)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "从以下描述中提取3个关键词。优先主谓宾结构，没有主谓宾时用3个最重要的词。只返回JSON：{\"kw1\":\"...\",\"kw2\":\"...\",\"kw3\":\"...\"}" },
            new() { Role = "user", Content = description }
        };
        var result = await CallLlmFunctionAsync(messages);
        return ParseKeywords(result);
    }

    public async Task<int> EvaluateImportanceAsync(string content)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "评估以下内容的重要度，返回0-100的整数。只返回数字，不要其他文字。评估标准：个人偏好>60，重要事件>70，强烈情绪>80，长期需求>90。" },
            new() { Role = "user", Content = content }
        };
        var result = await CallLlmFunctionAsync(messages);
        return int.TryParse(result.Trim(), out var score) ? score : 30;
    }

    // ========== 工具/插件/MCP调用 ==========

    public async Task<string> ProcessToolCallAsync(string toolName, string parameters)
    {
        var result = await _toolService.ExecuteToolAsync(toolName, parameters);
        return result.Success ? result.Data : $"错误：{result.Error}";
    }

    public async Task<string> ProcessPluginCallAsync(string pluginName, string parameters)
    {
        return $"插件 '{pluginName}' 暂未实现";
    }

    public async Task<string> ProcessMcpCallAsync(string serverName, string toolName, string parameters)
    {
        return $"MCP服务器 '{serverName}' 的工具 '{toolName}' 暂未实现";
    }

    // ========== 状态查询 ==========

    public AgentStatus GetStatus()
    {
        return new AgentStatus
        {
            CurrentMood = _moodTracker.CurrentMood.ToString(),
            ShortTermMemoryCount = _shortTermMemory.Count,
            MidTermMemoryCount = 0,
            LongTermMemoryCount = 0,
            IsProcessing = _isProcessing,
            LastEvent = _lastEvent
        };
    }

    // ========== 私有方法 ==========

    /// <summary>构建 System Prompt</summary>
    private string BuildSystemPrompt()
    {
        var name = _personality?.Name ?? "小琪";
        var personalityDesc = _personality?.Description ?? "温柔可爱，善解人意";
        var currentMood = _moodTracker.CurrentMood.ToString();

        // 基础工具描述
        var baseTools = _toolService.GetToolDefinitions();
        var baseToolsDesc = string.Join("\n", baseTools.Select(t =>
            $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"));

        // 心情附加工具描述
        var moodTools = _toolService.GetMoodBasedTools(_moodTracker.CurrentMood);
        var moodToolsDesc = string.Join("\n", moodTools.Select(t =>
            $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"));

        var formatter = new PromptFormatter(SystemPromptTemplate);
        return formatter.Format(new Dictionary<string, string>
        {
            { "Name", name },
            { "Personality", personalityDesc },
            { "CurrentMood", currentMood },
            { "BaseTools", baseToolsDesc },
            { "MoodTools", moodToolsDesc }
        });
    }

    /// <summary>构建用户上下文（含短期记忆）</summary>
    private async Task<string> BuildUserContextAsync(string userMessage)
    {
        // 短期记忆
        var recentMessages = _shortTermMemory.GetRecentMessages(10);
        var shortTermStr = string.Join("\n", recentMessages.Select(m => $"[{m.Role}] {m.Content}"));

        var formatter = new PromptFormatter(UserContextTemplate);
        return formatter.Format(new Dictionary<string, string>
        {
            { "ShortTermMemory", shortTermStr },
            { "UserMessage", userMessage }
        });
    }

    /// <summary>调用 LLM 对话模式</summary>
    private async Task<string> CallLlmChatAsync(List<ChatMessage> messages)
    {
        var provider = _appSettings.DefaultProvider;
        var model = _appSettings.ChatModel;

        var openAiMessages = messages.Select(m => m.Role switch
        {
            "system" => (OpenAiChatMessage)new OpenAiSystemChatMessage(m.Content),
            "user" => new OpenAiUserChatMessage(m.Content),
            "assistant" => new OpenAiAssistantChatMessage(m.Content),
            _ => new OpenAiUserChatMessage(m.Content)
        }).ToList();

        return await _llmClient.SendChatAsync(provider, model, openAiMessages);
    }

    /// <summary>调用 LLM 函数模式</summary>
    private async Task<string> CallLlmFunctionAsync(List<ChatMessage> messages)
    {
        var provider = _appSettings.DefaultProvider;
        var model = _appSettings.FunctionModel;

        var openAiMessages = messages.Select(m => m.Role switch
        {
            "system" => (OpenAiChatMessage)new OpenAiSystemChatMessage(m.Content),
            "user" => new OpenAiUserChatMessage(m.Content),
            "assistant" => new OpenAiAssistantChatMessage(m.Content),
            _ => new OpenAiUserChatMessage(m.Content)
        }).ToList();

        return await _llmClient.SendChatAsync(provider, model, openAiMessages);
    }

    /// <summary>解析 LLM 响应，提取回复文本和 actions</summary>
    private (string reply, List<AgentAction>? actions) ParseResponse(string response)
    {
        if (!_appSettings.EnableStructuredResponse)
        {
            return (response, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var reply = root.TryGetProperty("reply", out var replyElement)
                ? replyElement.GetString() ?? response
                : response;

            List<AgentAction>? actions = null;
            if (root.TryGetProperty("actions", out var actionsElement))
            {
                actions = JsonSerializer.Deserialize<List<AgentAction>>(actionsElement.GetRawText());
            }

            return (reply, actions);
        }
        catch (JsonException)
        {
            return (response, null);
        }
    }

    /// <summary>执行 actions 数组</summary>
    private async Task ExecuteActionsAsync(List<AgentAction> actions)
    {
        var maxActions = _appSettings.MaxActionsPerResponse;
        var count = 0;

        foreach (var action in actions)
        {
            if (count >= maxActions) break;
            count++;

            try
            {
                switch (action.Type)
                {
                    case "tool_call":
                        var toolResult = await _toolService.ExecuteToolAsync(
                            action.Name ?? "",
                            action.Parameters ?? "{}");
                        _shortTermMemory.AddMessage("system",
                            $"[工具执行] {action.Name}: {(toolResult.Success ? "成功" : $"失败: {toolResult.Error}")}");
                        break;

                    case "plugin_call":
                        var pluginResult = await ProcessPluginCallAsync(
                            action.Name ?? "",
                            action.Parameters ?? "{}");
                        _shortTermMemory.AddMessage("system", $"[插件执行] {action.Name}: {pluginResult}");
                        break;

                    case "mcp_call":
                        var mcpResult = await ProcessMcpCallAsync(
                            action.ServerName ?? "",
                            action.Name ?? "",
                            action.Parameters ?? "{}");
                        _shortTermMemory.AddMessage("system", $"[MCP执行] {action.ServerName}/{action.Name}: {mcpResult}");
                        break;

                    case "mood_change":
                        if (Enum.TryParse<AgentMood>(action.Mood, true, out var mood))
                        {
                            _moodTracker.SetMood(mood);
                        }
                        break;

                    case "midterm_memory":
                        if (_midTermMemory != null &&
                            _appSettings.EnableMidTermMemoryOnChat &&
                            !string.IsNullOrEmpty(action.Description))
                        {
                            var entry = new MidTermMemoryEntry
                            {
                                Description = action.Description,
                                Importance = action.Importance,
                                Source = "LLM",
                                Timestamp = DateTime.Now
                            };
                            await _midTermMemory.AddEntryAsync(entry);
                        }
                        break;

                    case "animation":
                        _lastEvent = $"animation:{action.Animation}";
                        break;
                }
            }
            catch (Exception ex)
            {
                _shortTermMemory.AddMessage("system", $"[执行错误] {action.Type}: {ex.Message}");
            }
        }
    }

    /// <summary>检查短期记忆是否溢出</summary>
    private async Task CheckMemoryOverflowAsync()
    {
        if (_shortTermMemory.Count >= _shortTermMemory.Capacity * 0.8)
        {
            var allMessages = _shortTermMemory.GetAllMessages();
            var chatHistory = string.Join("\n",
                allMessages.Take(allMessages.Count / 2)
                    .Select(m => $"{m.Role}: {m.Content}"));

            try
            {
                var summary = await SummarizeMemoryAsync(chatHistory);
                var importance = await EvaluateImportanceAsync(summary);

                if (importance > 30 && _midTermMemory != null)
                {
                    await _midTermMemory.AddEntryAsync(new MidTermMemoryEntry
                    {
                        Description = summary,
                        Importance = importance,
                        Source = "Overflow",
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>解析关键词 JSON</summary>
    private (string, string, string) ParseKeywords(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kw1 = root.TryGetProperty("kw1", out var k1) ? k1.GetString() ?? "" : "";
            var kw2 = root.TryGetProperty("kw2", out var k2) ? k2.GetString() ?? "" : "";
            var kw3 = root.TryGetProperty("kw3", out var k3) ? k3.GetString() ?? "" : "";
            return (kw1, kw2, kw3);
        }
        catch
        {
            return ("", "", "");
        }
    }

    /// <summary>简单关键词提取（用于长期记忆检索）</summary>
    private List<string> ExtractSimpleKeywords(string text)
    {
        var separators = new[] { ' ', '，', '。', '！', '？', '、', '；', '：', '\n', '\r', '\t' };
        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .Distinct()
            .Take(5)
            .ToList();
    }
}

/// <summary>
/// Agent 动作模型（从 LLM 响应中解析）
/// </summary>
public class AgentAction
{
    public string Type { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ServerName { get; set; }
    public string? Parameters { get; set; }
    public string? Mood { get; set; }
    public string? Description { get; set; }
    public int Importance { get; set; }
    public string? Animation { get; set; }
}
