using System.Text.Json;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Events;
using MochiBot.Src.Models;
using MochiBot.Src.Services.Tool;
using MochiBot.Src.Services;
using OpenAiChatMessage = OpenAI.Chat.ChatMessage;
using OpenAiSystemChatMessage = OpenAI.Chat.SystemChatMessage;
using OpenAiUserChatMessage = OpenAI.Chat.UserChatMessage;
using OpenAiAssistantChatMessage = OpenAI.Chat.AssistantChatMessage;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// Agent 核心协调层实现
    /// 通过事件调度器接收事件，处理完成后发布回复事件
    /// </summary>
    public class MainAgent : IAgent
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly LlmClient _llmClient;
        private readonly IShortTermMemory _shortTermMemory;
        private readonly IToolService _toolService;
        private readonly IDatabaseService? _databaseService;
        private readonly AppSettings _appSettings;
        private readonly PersonalityConfig? _personality;

        // 事件订阅ID
        private readonly List<string> _subscriptionIds = new();

        // ========== 心情记录器（集成到 Agent 内部） ==========
        private AgentMood _currentMood = AgentMood.Neutral;

        private readonly ActionExecutor _actionExecutor;
        private readonly PromptFormatter _systemPromptFormatter;
        private readonly PromptFormatter _userContextFormatter;
        private bool _isProcessing;
        private string _lastEvent = string.Empty;
        private string _lastJsonError = string.Empty;

        // 对话模式 System Prompt 模板
        private const string SystemPromptTemplate = @"
你是一个名叫{Name}，你的性格是{Personality}。
你的主人（用户）叫{UserName}，请用这个名字称呼。
【当前情绪】{CurrentMood}

【可用工具】{BaseTools}

【心情附加工具（当前情绪可用）】{MoodTools}

{FormatInstruction}
";

        // 对话模式用户上下文模板
        private const string UserContextTemplate = @"
【短期记忆】{ShortTermMemory}

用户说：{UserMessage}

请通过 actions 中的 reply 工具来回复用户，不要直接在 reply 字段中写回复。
";

        public MainAgent(
            IEventDispatcher eventDispatcher,
            LlmClient llmClient,
            IConfigReader configReader,
            IShortTermMemory shortTermMemory,
            IToolService toolService,
            IDatabaseService? databaseService = null)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _shortTermMemory = shortTermMemory ?? throw new ArgumentNullException(nameof(shortTermMemory));
            _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
            _databaseService = databaseService;

            _appSettings = configReader.GetAppSettings();
            _personality = configReader.GetActivePersonality();

            // 创建 ActionExecutor，将 actions 执行逻辑委托给它
            _actionExecutor = new ActionExecutor(
                _toolService,
                mood => ChangeMoodByEvent(mood.ToString()),
                (desc, param) => _shortTermMemory.AddMessage("system", $"[中期记忆] {desc}"),
                anim => _lastEvent = $"animation:{anim}");

            _systemPromptFormatter = new PromptFormatter(SystemPromptTemplate);
            _userContextFormatter = new PromptFormatter(UserContextTemplate);

            // 订阅事件调度器
            SubscribeToEvents();
        }

        /// <summary>订阅事件调度器的事件</summary>
        private void SubscribeToEvents()
        {
            // 订阅用户输入事件（异步处理器）
            var userSubId = _eventDispatcher.Subscribe(EventCategory.UserInput, ProcessEventAsync);
            _subscriptionIds.Add(userSubId);

            // 订阅系统自动事件（异步处理器）
            var sysSubId = _eventDispatcher.Subscribe(EventCategory.SystemAuto, ProcessEventAsync);
            _subscriptionIds.Add(sysSubId);

            // 订阅 UI 交互事件（摸摸、点击等）
            var uiSubId = _eventDispatcher.Subscribe(EventCategory.UiInteraction, (eventData) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(eventData.Info);
                    if (doc.RootElement.TryGetProperty("type", out var typeProp))
                    {
                        var uiType = typeProp.GetString();
                        if (uiType == "pet")
                        {
                            _lastEvent = "Pet";
                            ChangeMoodByEvent("Pet");
                        }
                    }
                }
                catch { }
            });
            _subscriptionIds.Add(uiSubId);
        }

        // ========== 心情记录器（集成到 Agent 内部） ==========

        /// <summary>获取当前情绪</summary>
        public AgentMood CurrentMood => _currentMood;

        /// <summary>根据事件类型切换心情，并通过事件调度器发布 MoodChange 事件</summary>
        private void ChangeMoodByEvent(string eventType)
        {
            var newMood = eventType switch
            {
                "LateNight" or "Sleepy" => AgentMood.Sleepy,
                "LongWork" => AgentMood.Neutral,
                "Idle" => AgentMood.Sad,
                "Active" => AgentMood.Neutral,
                "Pet" => AgentMood.Touched,
                "Compliment" => AgentMood.Happy,
                "Angry" => AgentMood.Angry,
                _ => _currentMood
            };

            if (_currentMood == newMood) return;
            _currentMood = newMood;

            // 通过事件调度器发布情绪变化事件
            _eventDispatcher.Publish(new EventData
            {
                Category = EventCategory.MoodChange,
                Trigger = EventTrigger.System,
                Info = JsonSerializer.Serialize(new
                {
                    mood = newMood.ToString(),
                    source = eventType
                })
            });

            // 记录到数据库
            if (_databaseService != null)
            {
                _ = _databaseService.LogMoodChangeAsync(newMood, eventType);
            }
        }

        private readonly Random _random = new();

        // ========== 统一事件处理 ==========

        public async Task ProcessEventAsync(EventData eventData)
        {
            _isProcessing = true;
            _lastEvent = eventData.Category.ToString();

            try
            {
                // 检查是否为碎碎念事件，根据权重决定使用内置文本还是 LLM
                if (eventData.Category == EventCategory.SystemAuto && TryHandleMurmur(eventData))
                    return;

                string userMessage;

                // 根据事件分类构建用户消息
                switch (eventData.Category)
                {
                    case EventCategory.UserInput:
                        userMessage = eventData.Info;
                        break;

                    case EventCategory.SystemAuto:
                        userMessage = BuildAutoEventPrompt(eventData);
                        break;

                    default:
                        return;
                }

                await ProcessWithLlmAsync(eventData, userMessage);

                // 更新情绪（根据用户交互内容和时间自动判断）
                if (eventData.Category == EventCategory.UserInput)
                {
                    DetectAndTriggerMoodEvent(userMessage);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// 尝试处理碎碎念事件
        /// 根据权重和随机决定使用内置文本还是 LLM 生成回复
        /// </summary>
        /// <returns>true 表示已处理（无需继续处理），false 表示不是碎碎念事件</returns>
        private bool TryHandleMurmur(EventData eventData)
        {
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                    return false;

                var type = typeProp.GetString();
                if (type != "murmur")
                    return false;

                // 从 parameters 中读取权重
                int weight = 30;
                if (doc.RootElement.TryGetProperty("parameters", out var paramsProp))
                {
                    int.TryParse(paramsProp.GetString(), out weight);
                }

                // 根据权重随机决定：roll < weight 时使用 LLM，否则使用内置文本
                var roll = _random.Next(100);
                if (roll < weight)
                {
                    // 使用 LLM 生成回复（返回 false 让 ProcessEventAsync 继续处理）
                    return false;
                }

                // 使用内置碎碎念文本（通过 ToolService 统一工具接口）
                var result = _toolService.ExecuteToolAsync("murmur", "{}").GetAwaiter().GetResult();
                if (result.Success)
                {
                    using var resultDoc = JsonDocument.Parse(result.Data);
                    var text = resultDoc.RootElement.TryGetProperty("murmur", out var murmurProp)
                        ? murmurProp.GetString() ?? ""
                        : "";

                    if (!string.IsNullOrEmpty(text))
                    {
                        _shortTermMemory.AddMessage("assistant", text);
                        _eventDispatcher.Publish(new EventData
                        {
                            Category = EventCategory.ToolResult,
                            Trigger = EventTrigger.System,
                            Info = JsonSerializer.Serialize(new
                            {
                                type = "reply",
                                content = text,
                                source = "murmur"
                            })
                        });
                    }
                }

                return true; // 已处理，无需继续
            }
            catch
            {
                return false; // 解析失败，交给 LLM 处理
            }
        }

        /// <summary>使用 LLM 处理事件</summary>
        private async Task ProcessWithLlmAsync(EventData eventData, string? userMessage = null)
        {
            if (userMessage == null)
            {
                userMessage = eventData.Category switch
                {
                    EventCategory.UserInput => eventData.Info,
                    EventCategory.SystemAuto => BuildAutoEventPrompt(eventData),
                    _ => eventData.Info
                };
            }

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

            // 4. 解析 LLM 响应，提取 actions 和可能的 fallback 回复
            var (fallbackReply, actions) = ParseResponse(response);

            // 5. 执行 actions，从中提取 reply 工具的回复文本
            var reply = await ExecuteActionsAsync(actions);

            // 6. 如果 actions 中没有 reply 但有 fallback 回复（JSON解析失败时），使用 fallback
            if (string.IsNullOrEmpty(reply) && !string.IsNullOrEmpty(fallbackReply) && fallbackReply != response)
            {
                reply = fallbackReply;
            }

            // 7. 如果有回复，记录到短期记忆并发布回复事件
            if (!string.IsNullOrEmpty(reply))
            {
                _shortTermMemory.AddMessage("assistant", reply);

                // 发布回复事件，供 UI 订阅显示
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.ToolResult,
                    Trigger = EventTrigger.Llm,
                    Info = JsonSerializer.Serialize(new
                    {
                        type = "reply",
                        content = reply,
                        source = eventData.Category.ToString()
                    })
                });
            }
        }

        /// <summary>根据系统自动事件类型构建 Prompt</summary>
        private static string BuildAutoEventPrompt(EventData eventData)
        {
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (doc.RootElement.TryGetProperty("type", out var typeProp))
                {
                    var type = typeProp.GetString();
                    return type switch
                    {
                        "murmur" => "你现在想对用户说一句碎碎念/撒娇的话，表达你的思念或关心。",
                        "eye_rest" => "用户已经盯着屏幕很久了，提醒他休息一下眼睛。",
                        "late_night" => "已经很晚了，关心用户为什么还没睡，温柔地催他睡觉。",
                        _ => $"事件类型：{type}。请根据这个事件生成合适的回复。"
                    };
                }
            }
            catch { }

            return "请根据事件生成合适的回复。";
        }

        // ========== 状态查询 ==========

        public AgentStatus GetStatus()
        {
            return new AgentStatus
            {
                CurrentMood = _currentMood.ToString(),
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
            var userName = _appSettings.UserName;

            // 基础工具描述
            var baseTools = _toolService.GetToolDefinitions();
            var baseToolsDesc = string.Join("\n", baseTools.Select(t =>
                $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"));

            // 心情附加工具描述
            var moodTools = _toolService.GetMoodBasedTools(_currentMood);
            var moodToolsDesc = string.Join("\n", moodTools.Select(t =>
                $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"));

            // 工具调用格式说明
            var formatInstruction = _toolService.GetFormatInstruction();

            return _systemPromptFormatter.Format(new Dictionary<string, string>
            {
                { "Name", name },
                { "Personality", personalityDesc },
                { "UserName", userName },
                { "CurrentMood", $"{_currentMood}" },
                { "BaseTools", baseToolsDesc },
                { "MoodTools", moodToolsDesc },
                { "FormatInstruction", formatInstruction }
            });
        }

        /// <summary>构建用户上下文（含短期记忆）</summary>
        private Task<string> BuildUserContextAsync(string userMessage)
        {
            // 短期记忆
            var recentMessages = _shortTermMemory.GetRecentMessages(10);
            var shortTermStr = string.Join("\n", recentMessages.Select(m => $"[{m.Role}] {m.Content}"));

            var result = _userContextFormatter.Format(new Dictionary<string, string>
            {
                { "ShortTermMemory", shortTermStr },
                { "UserMessage", userMessage }
            });

            // 如果有最近一次 JSON 解析错误，追加到 Prompt 末尾提醒 LLM
            if (!string.IsNullOrEmpty(_lastJsonError))
            {
                result += $"\n\n{_lastJsonError}";
            }

            return Task.FromResult(result);
        }

        /// <summary>从模型名中提取提供商（格式："{提供商}/{模型名}"）</summary>
        private static (string provider, string model) ParseModelName(string modelFullName)
        {
            if (string.IsNullOrEmpty(modelFullName) || modelFullName == "default")
                return ("LocalLMStudio", "default");

            var parts = modelFullName.Split('/', 2);
            if (parts.Length == 2)
                return (parts[0], parts[1]);

            return ("LocalLMStudio", modelFullName);
        }

        /// <summary>获取对话模型名称（优先从人格配置读取）</summary>
        private string GetChatModel()
        {
            // 从人格配置的第一个子人格中获取模型
            if (_personality?.Personalities?.Count > 0 &&
                _personality.Personalities[0].ChatModels?.Count > 0)
            {
                return _personality.Personalities[0].ChatModels[0];
            }
            return "default";
        }

        /// <summary>调用 LLM 对话模式</summary>
        private async Task<string> CallLlmChatAsync(List<ChatMessage> messages)
        {
            var (provider, model) = ParseModelName(GetChatModel());

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
                // 使用 ActionExecutor 的静态方法解析
                var reply = ActionExecutor.ExtractReply(response) ?? response;
                var actions = ActionExecutor.ParseActions(response);

                return (reply, actions);
            }
            catch (JsonException ex)
            {
                // 记录最近一次 JSON 解析错误，下次 Prompt 会追加提醒
                _lastJsonError = $"[JSON解析错误] 你的返回格式有误，请严格按照要求的JSON格式返回。错误: {ex.Message}";

                // 返回原始响应作为 fallback 回复
                return (response, null);
            }
        }

        /// <summary>执行 actions 数组，返回从 reply 工具中提取的回复文本（如果没有 reply 则返回空字符串）</summary>
        private async Task<string> ExecuteActionsAsync(List<AgentAction>? actions)
        {
            var replyText = await _actionExecutor.ExecuteActionsAsync(actions, _appSettings.MaxActionsPerResponse);

            // 记录工具执行结果到短期记忆
            if (actions != null)
            {
                foreach (var action in actions)
                {
                    if (action.Type == "tool_call" && action.Name != "reply")
                    {
                        _shortTermMemory.AddMessage("system", $"[工具执行] {action.Name}");
                    }
                    else if (action.Type == "plugin_call")
                    {
                        _shortTermMemory.AddMessage("system", $"[插件执行] {action.Name}");
                    }
                    else if (action.Type == "mcp_call")
                    {
                        _shortTermMemory.AddMessage("system", $"[MCP执行] {action.ServerName}/{action.Name}");
                    }
                }
            }

            return replyText;
        }

        /// <summary>根据用户消息内容和时间自动检测并触发情绪事件</summary>
        private void DetectAndTriggerMoodEvent(string userMessage)
        {
            var hour = DateTime.Now.Hour;
            if (hour >= 23 || hour < 6)
            {
                _lastEvent = "LateNight";
                ChangeMoodByEvent("LateNight");
                return;
            }

            var msg = userMessage.ToLowerInvariant();

            if (msg.Contains("摸摸") || msg.Contains("摸头") || msg.Contains("拍头") || msg.Contains("抱抱"))
            {
                _lastEvent = "Pet";
                ChangeMoodByEvent("Pet");
                return;
            }

            if (msg.Contains("夸") || msg.Contains("好看") || msg.Contains("可爱") || msg.Contains("漂亮") ||
                msg.Contains("喜欢你") || msg.Contains("真棒") || msg.Contains("厉害"))
            {
                _lastEvent = "Compliment";
                ChangeMoodByEvent("Compliment");
                return;
            }

            _lastEvent = "Active";
            ChangeMoodByEvent("Active");
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
}
