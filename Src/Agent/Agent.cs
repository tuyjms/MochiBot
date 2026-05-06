using System.Text.Json;
using catgirlwindow.Src.Core.Config;
using catgirlwindow.Src.Core.Config.Models;
using catgirlwindow.Src.Core.Database;
using catgirlwindow.Src.Core.Events;
using catgirlwindow.Src.Models;
using catgirlwindow.Src.Services;
using OpenAiChatMessage = OpenAI.Chat.ChatMessage;
using OpenAiSystemChatMessage = OpenAI.Chat.SystemChatMessage;
using OpenAiUserChatMessage = OpenAI.Chat.UserChatMessage;
using OpenAiAssistantChatMessage = OpenAI.Chat.AssistantChatMessage;

namespace catgirlwindow.Src.Agent
{
    /// <summary>
    /// Agent 核心协调层实现
    /// 通过事件调度器接收事件，不再直接接收方法调用
    /// </summary>
    public class MainAgent : IAgent
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly LlmClient _llmClient;
        private readonly IConfigReader _configReader;
        private readonly IShortTermMemory _shortTermMemory;
        private readonly IToolService _toolService;
        private readonly IDatabaseService? _databaseService;
        private readonly AppSettings _appSettings;
        private readonly PersonalityConfig? _personality;

        // 预留依赖注入
#pragma warning disable CS0169
        private readonly IPromptFormatter _formatter;
#pragma warning restore CS0169

        // 事件订阅ID
        private readonly List<string> _subscriptionIds = new();

        // ========== 心情记录器（集成到 Agent 内部） ==========
        private AgentMood _currentMood = AgentMood.Neutral;
        /// <summary>情绪变化时触发的事件（UI订阅以更新头像）</summary>
        public event EventHandler<AgentMood>? MoodChanged;

        private bool _isProcessing;
        private string _lastEvent = string.Empty;
        private string _lastJsonError = string.Empty;

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
";

        // 对话模式用户上下文模板
        private const string UserContextTemplate = @"
【短期记忆】
{ShortTermMemory}

用户说：{UserMessage}

请通过 actions 中的 reply 工具来回复用户，不要直接在 reply 字段中写回复。
";

        public MainAgent(
            IEventDispatcher eventDispatcher,
            LlmClient llmClient,
            IConfigReader configReader,
            IPromptFormatter formatter,
            IShortTermMemory shortTermMemory,
            IToolService toolService,
            IDatabaseService? databaseService = null)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _shortTermMemory = shortTermMemory ?? throw new ArgumentNullException(nameof(shortTermMemory));
            _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
            _databaseService = databaseService;

            _appSettings = configReader.GetAppSettings();
            _personality = configReader.GetActivePersonality();

            // 订阅事件调度器
            SubscribeToEvents();
        }

        /// <summary>订阅事件调度器的事件</summary>
        private void SubscribeToEvents()
        {
            // 订阅系统自动事件（碎碎念、用眼提醒、深夜关怀）
            var sysSubId = _eventDispatcher.Subscribe(EventCategory.SystemAuto, async (eventData) =>
            {
                try
                {
                    string? eventType = null;
                    string? eventInfo = null;
                    using (var doc = JsonDocument.Parse(eventData.Info))
                    {
                        if (doc.RootElement.TryGetProperty("type", out var typeProp))
                            eventType = typeProp.GetString();
                        if (doc.RootElement.TryGetProperty("hours", out var hoursProp))
                            eventInfo = hoursProp.GetInt32().ToString();
                    }

                    if (!string.IsNullOrEmpty(eventType))
                    {
                        await ProcessAutoEventAsync(eventType, eventInfo);
                    }
                }
                catch { }
            });
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
                            UpdateMoodByEvent("Pet");
                        }
                    }
                }
                catch { }
            });
            _subscriptionIds.Add(uiSubId);
        }

        // ========== 心情记录器方法（集成到 Agent 内部） ==========

        /// <summary>获取当前情绪</summary>
        public AgentMood CurrentMood => _currentMood;

        /// <summary>手动设置情绪（外部触发，如摸摸她）</summary>
        public void SetMood(AgentMood mood)
        {
            if (_currentMood == mood) return;
            _currentMood = mood;
            MoodChanged?.Invoke(this, mood);

            // 记录到数据库
            if (_databaseService != null)
            {
                _ = _databaseService.LogMoodChangeAsync(mood, _lastEvent);
            }
        }

        /// <summary>根据系统事件自动切换情绪</summary>
        public void UpdateMoodByEvent(string eventType)
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

            SetMood(newMood);
        }

        /// <summary>获取当前情绪对应的表情图片路径</summary>
        public string GetMoodImagePath()
        {
            return _currentMood switch
            {
                AgentMood.Happy => "Resources/Images/happy.png",
                AgentMood.Sad => "Resources/Images/sad.png",
                AgentMood.Sleepy => "Resources/Images/sleepy.png",
                AgentMood.Touched => "Resources/Images/touched.png",
                AgentMood.Angry => "Resources/Images/angry.png",
                _ => "Resources/Images/neutral.png"
            };
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

                // 4. 解析 LLM 响应，提取 actions 和可能的 fallback 回复
                var (fallbackReply, actions) = ParseResponse(response);

                // 5. 执行 actions，从中提取 reply 工具的回复文本
                var reply = await ExecuteActionsAsync(actions);

                // 6. 如果 actions 中没有 reply 但有 fallback 回复（JSON解析失败时），使用 fallback
                if (string.IsNullOrEmpty(reply) && !string.IsNullOrEmpty(fallbackReply) && fallbackReply != response)
                {
                    reply = fallbackReply;
                }

                // 7. 如果有回复，记录到短期记忆
                if (!string.IsNullOrEmpty(reply))
                {
                    _shortTermMemory.AddMessage("assistant", reply);
                }

                // 8. 检查短期记忆是否溢出
                await CheckMemoryOverflowAsync();

                // 9. 更新情绪（根据用户交互内容和时间自动判断）
                DetectAndTriggerMoodEvent(userMessage);

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

                // 解析 LLM 响应，提取 actions
                var (_, actions) = ParseResponse(response);

                // 执行 actions，从中提取 reply 工具的回复文本
                var reply = await ExecuteActionsAsync(actions);

                // 如果有回复，记录到短期记忆
                if (!string.IsNullOrEmpty(reply))
                {
                    _shortTermMemory.AddMessage("assistant", reply);
                }

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

        public Task<string> ProcessPluginCallAsync(string pluginName, string parameters)
        {
            return Task.FromResult($"插件 '{pluginName}' 暂未实现");
        }

        public Task<string> ProcessMcpCallAsync(string serverName, string toolName, string parameters)
        {
            return Task.FromResult($"MCP服务器 '{serverName}' 的工具 '{toolName}' 暂未实现");
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
            var moodDesc = GetMoodDescription(_currentMood);

            // 基础工具描述
            var baseTools = _toolService.GetToolDefinitions();
            var baseToolsDesc = string.Join("\n", baseTools.Select(t =>
                $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"));

            // 心情附加工具描述
            var moodTools = _toolService.GetMoodBasedTools(_currentMood);
            var moodToolsDesc = string.Join("\n", moodTools.Select(t =>
                $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"));

            var formatter = new PromptFormatter(SystemPromptTemplate);
            return formatter.Format(new Dictionary<string, string>
            {
                { "Name", name },
                { "Personality", personalityDesc },
                { "CurrentMood", $"{_currentMood} - {moodDesc}" },
                { "BaseTools", baseToolsDesc },
                { "MoodTools", moodToolsDesc }
            });
        }

        /// <summary>构建用户上下文（含短期记忆）</summary>
        private Task<string> BuildUserContextAsync(string userMessage)
        {
            // 短期记忆
            var recentMessages = _shortTermMemory.GetRecentMessages(10);
            var shortTermStr = string.Join("\n", recentMessages.Select(m => $"[{m.Role}] {m.Content}"));

            var formatter = new PromptFormatter(UserContextTemplate);
            var result = formatter.Format(new Dictionary<string, string>
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

        /// <summary>获取函数模型名称（优先从人格配置读取，没有则用对话模型）</summary>
        private string GetFunctionModel()
        {
            // 从人格配置的第一个子人格中获取函数模型，没有则用对话模型
            if (_personality?.Personalities?.Count > 0)
            {
                var sub = _personality.Personalities[0];
                if (sub.FunctionModels?.Count > 0)
                    return sub.FunctionModels[0];
                if (sub.ChatModels?.Count > 0)
                    return sub.ChatModels[0];
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

        /// <summary>调用 LLM 函数模式</summary>
        private async Task<string> CallLlmFunctionAsync(List<ChatMessage> messages)
        {
            var (provider, model) = ParseModelName(GetFunctionModel());

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
                    actions = ParseActionsArray(actionsElement);
                }

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
            var replyText = string.Empty;
            if (actions == null || actions.Count == 0) return replyText;

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
                            // 如果是 reply 工具，提取回复文本
                            if (action.Name == "reply" && !string.IsNullOrEmpty(action.Parameters))
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(action.Parameters);
                                    if (doc.RootElement.TryGetProperty("reply_text", out var replyElement))
                                    {
                                        replyText = replyElement.GetString() ?? string.Empty;
                                    }
                                }
                                catch
                                {
                                    // 解析失败则忽略
                                }
                            }
                            else
                            {
                                var toolResult = await _toolService.ExecuteToolAsync(
                                    action.Name ?? "",
                                    action.Parameters ?? "{}");
                                _shortTermMemory.AddMessage("system",
                                    $"[工具执行] {action.Name}: {(toolResult.Success ? "成功" : $"失败: {toolResult.Error}")}");
                            }
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
                                SetMood(mood);
                            }
                            break;

                        case "midterm_memory":
                            // 中期记忆已合并到长期记忆模块，暂不处理
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

            return replyText;
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

                    if (importance > 30)
                    {
                        // 记录到短期记忆作为 system 消息
                        _shortTermMemory.AddMessage("system", $"[记忆摘要] {summary}");
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>手动解析 actions 数组，兼容 parameters 为对象的情况</summary>
        private static List<AgentAction>? ParseActionsArray(JsonElement actionsElement)
        {
            if (actionsElement.ValueKind != JsonValueKind.Array) return null;

            var actions = new List<AgentAction>();
            foreach (var item in actionsElement.EnumerateArray())
            {
                var action = new AgentAction();

                if (item.TryGetProperty("type", out var typeProp))
                    action.Type = typeProp.GetString() ?? string.Empty;

                if (item.TryGetProperty("name", out var nameProp))
                    action.Name = nameProp.GetString();

                if (item.TryGetProperty("server_name", out var serverProp))
                    action.ServerName = serverProp.GetString();

                if (item.TryGetProperty("mood", out var moodProp))
                    action.Mood = moodProp.GetString();

                if (item.TryGetProperty("description", out var descProp))
                    action.Description = descProp.GetString();

                if (item.TryGetProperty("animation", out var animProp))
                    action.Animation = animProp.GetString();

                if (item.TryGetProperty("importance", out var impProp))
                    action.Importance = impProp.GetInt32();

                // parameters 可能是对象或字符串，统一转为 JSON 字符串
                if (item.TryGetProperty("parameters", out var paramsProp))
                {
                    action.Parameters = paramsProp.ValueKind switch
                    {
                        JsonValueKind.String => paramsProp.GetString(),
                        JsonValueKind.Object or JsonValueKind.Array => paramsProp.GetRawText(),
                        _ => paramsProp.GetRawText()
                    };
                }

                actions.Add(action);
            }

            return actions;
        }

        /// <summary>解析关键词 JSON</summary>
        private static (string, string, string) ParseKeywords(string json)
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

        /// <summary>根据用户消息内容和时间自动检测并触发情绪事件</summary>
        private void DetectAndTriggerMoodEvent(string userMessage)
        {
            var hour = DateTime.Now.Hour;
            if (hour >= 23 || hour < 6)
            {
                _lastEvent = "LateNight";
                UpdateMoodByEvent("LateNight");
                return;
            }

            var msg = userMessage.ToLowerInvariant();

            if (msg.Contains("摸摸") || msg.Contains("摸头") || msg.Contains("拍头") || msg.Contains("抱抱"))
            {
                _lastEvent = "Pet";
                UpdateMoodByEvent("Pet");
                return;
            }

            if (msg.Contains("夸") || msg.Contains("好看") || msg.Contains("可爱") || msg.Contains("漂亮") ||
                msg.Contains("喜欢你") || msg.Contains("真棒") || msg.Contains("厉害"))
            {
                _lastEvent = "Compliment";
                UpdateMoodByEvent("Compliment");
                return;
            }

            _lastEvent = "Active";
            UpdateMoodByEvent("Active");
        }

        /// <summary>获取当前情绪的中文描述，用于注入 LLM 系统提示词</summary>
        private static string GetMoodDescription(AgentMood mood)
        {
            return mood switch
            {
                AgentMood.Happy => "开心 - 被夸奖或互动后感到愉快，表现活泼亲昵、主动多话、撒娇粘人",
                AgentMood.Sad => "委屈 - 长时间未被关注感到失落，回复简短小声、流露委屈感",
                AgentMood.Sleepy => "困倦 - 深夜时段感到困倦，语气慵懒、想睡觉",
                AgentMood.Touched => "感动 - 被温柔对待后深受感动，语气轻柔内敛、表达感激",
                AgentMood.Angry => "生气 - 被频繁打扰感到不耐烦，回复极简敷衍、语气消极",
                AgentMood.Teasing => "调皮 - 调侃互动状态，语气俏皮带点毒舌",
                AgentMood.Surprised => "惊讶 - 遇到意外情况，语气充满好奇和惊讶",
                _ => "平静 - 默认状态，温和耐心、主动亲近的正常对话"
            };
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
