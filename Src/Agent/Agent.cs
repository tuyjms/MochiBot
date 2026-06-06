using System.Text.Json;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using static MochiBot.Src.Core.Constants;
using static MochiBot.Src.EventModels.MoodEventTypes;
using LlmClient = MochiBot.Src.Services.LlmClient;
using OpenAiChatMessage = OpenAI.Chat.ChatMessage;
using OpenAiSystemChatMessage = OpenAI.Chat.SystemChatMessage;
using OpenAiUserChatMessage = OpenAI.Chat.UserChatMessage;
using OpenAiAssistantChatMessage = OpenAI.Chat.AssistantChatMessage;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// Agent 核心协调层实现
    /// 通过事件调度器接收事件，处理完成后发布回复事件
    /// 自管理 LlmClient、ShortTermMemory 和 LongMemory
    /// </summary>
    public class MainAgent : IAgent, IDisposable
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;
        private LlmClient _chatLlmClient;
        private readonly IToolService _toolService;
        private MoodManager _moodManager;
        private AppSettings _appSettings;
        private PersonalityConfig? _personality;
        private SubPersonality? _currentSubPersonality;

        // 事件订阅ID
        private readonly List<string> _subscriptionIds = new();

        private ActionExecutor _actionExecutor;
        private readonly PromptBuilder _promptBuilder;
        private readonly EventProcessingQueue _eventQueue;
        private readonly AutoEventFilter _autoEventFilter;
        private VisionService _visionService;
        private readonly MemoryCoordinator _memoryCoordinator;
        private readonly ChatHistoryRepository _chatHistoryRepo;
        private string _lastJsonError = string.Empty;

        private string _functionProviderName = string.Empty;
        private string _functionModelName = string.Empty;

        public MainAgent(
            IEventDispatcher eventDispatcher,
            IConfigReader configReader,
            IToolService toolService,
            ChatHistoryRepository chatHistoryRepo,
            MoodLogRepository? moodLogRepository = null)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
            _chatHistoryRepo = chatHistoryRepo ?? throw new ArgumentNullException(nameof(chatHistoryRepo));
            _moodManager = new MoodManager(eventDispatcher, moodLogRepository);

            _appSettings = configReader.GetAppSettings();
            _personality = configReader.GetActivePersonality();

            // 选择当前子人格（按权重概率）
            _currentSubPersonality = SelectSubPersonalityByWeight();

            // 自创建 LlmClient（对话模型）
            _chatLlmClient = CreateChatLlmClient();
            ResolveFunctionModel();

            // 创建记忆协调器（管理短期/长期记忆）
            _memoryCoordinator = new MemoryCoordinator(_functionProviderName, _functionModelName, _configReader);

            _promptBuilder = new PromptBuilder(_toolService);

            // 创建自动事件过滤器（内置任务条件判断）
            _autoEventFilter = new AutoEventFilter(
                _toolService,
                (role, content) => _memoryCoordinator.ShortTermMemory.AddMessage(role, content),
                evt => _eventDispatcher.Publish(evt));

            // 创建视觉服务（截图 → VisionModel → 文字描述）
            _visionService = new VisionService(configReader);

            // 创建事件处理队列（实际处理逻辑委托给 ProcessEventInternalAsync）
            _eventQueue = new EventProcessingQueue(_eventDispatcher, _configReader, ProcessEventInternalAsync);

            // 创建 ActionExecutor，将 actions 执行逻辑委托给它
            _actionExecutor = new ActionExecutor(
                _toolService,
                mood => _moodManager.ChangeMoodByEvent(mood.ToString()),
                (tag, name) => _memoryCoordinator.ShortTermMemory.AddMessage(ChatRoles.System, $"{tag} {name}"),
                anim =>
                {
                    _eventQueue.LastEvent = $"animation:{anim}";
                    _eventDispatcher.Publish(new EventData
                    {
                        Category = EventCategory.MoodChange,
                        Trigger = EventTrigger.Tool,
                        Info = JsonSerializer.Serialize(new
                        {
                            animation = anim,
                            source = EventSources.Tool
                        })
                    });
                });

            // 订阅事件调度器
            SubscribeToEvents();

            // 注册模块状态
            _eventDispatcher.RegisterModule("agent", AgentState.Idle.ToString().ToLower());

            // 启动长期记忆维护定时器
            _memoryCoordinator.StartMaintenanceTimer();
        }

        /// <summary>订阅事件调度器的事件</summary>
        private void SubscribeToEvents()
        {
            // 订阅用户输入事件（异步处理器）
            var userSubId = _eventDispatcher.Subscribe(EventCategory.UserInput, _eventQueue.EnqueueEventAsync);
            _subscriptionIds.Add(userSubId);

            // 订阅系统自动事件（异步处理器）
            var sysSubId = _eventDispatcher.Subscribe(EventCategory.SystemAuto, _eventQueue.EnqueueEventAsync);
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
                        if (uiType == UiInteractionTypes.Pet)
                        {
                            _eventQueue.LastEvent = Pet;
                            _moodManager.ChangeMoodByEvent(Pet);
                        }
                    }
                }
                catch { }
            });
            _subscriptionIds.Add(uiSubId);

            // 订阅配置变更事件（热重载）
            var configChangedSubId = _eventDispatcher.Subscribe(EventCategory.ConfigChanged, OnConfigChanged);
            _subscriptionIds.Add(configChangedSubId);
        }

        /// <summary>处理配置变更事件（热重载）</summary>
        private void OnConfigChanged(EventData eventData)
        {
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                var root = doc.RootElement;

                if (!root.TryGetProperty("changedItems", out var changedItems))
                    return;

                var items = new List<string>();
                foreach (var item in changedItems.EnumerateArray())
                {
                    items.Add(item.GetString() ?? "");
                }

                // 刷新 AppSettings
                _appSettings = _configReader.GetAppSettings();

                // 检查 ProviderConfig 是否变更（需要重建 LlmClient）
                if (items.Contains("ProviderConfig"))
                {
                    _chatLlmClient = CreateChatLlmClient();
                    ResolveFunctionModel();
                    _memoryCoordinator.RebuildMemories(_functionProviderName, _functionModelName);
                    RebuildActionExecutor();

                    _configReader.Logger.Info("[Agent] ProviderConfig 已变更，所有 LlmClient 和记忆模块已重建");
                }

                // 检查人格文件是否切换（需要重建 LlmClient + VisionService）
                if (items.Contains("ActivePersonality"))
                {
                    _personality = _configReader.GetActivePersonality();
                    _currentSubPersonality = SelectSubPersonalityByWeight();

                    // 人格切换可能改变 ChatModels/VisionModels/FunctionModels
                    _chatLlmClient = CreateChatLlmClient();
                    _visionService = new VisionService(_configReader);
                    ResolveFunctionModel();
                    _memoryCoordinator.RebuildMemories(_functionProviderName, _functionModelName);
                    RebuildActionExecutor();

                    _configReader.Logger.Info("[Agent] ActivePersonality 已切换，LlmClient + VisionService + 记忆模块已重建");
                }

                // 检查人格配置内容是否变更（刷新描述 + 重建 VisionService）
                if (items.Contains("PersonalityConfig"))
                {
                    _personality = _configReader.GetActivePersonality();
                    _currentSubPersonality = SelectSubPersonalityByWeight();
                    // VisionModels/ChatModels 可能已变更，重建 VisionService
                    _visionService = new VisionService(_configReader);
                    _configReader.Logger.Info("[Agent] 人格配置已刷新，VisionService 已重建");
                }

                // 检查模块设置是否变更（记忆参数 + 维护定时器）
                if (items.Contains("ModuleSettings"))
                {
                    if (_personality != null)
                        _memoryCoordinator.UpdateCapacity(_personality.MaxMessages);
                    _memoryCoordinator.UpdateOverflowStrategy();
                    _memoryCoordinator.RestartMaintenanceTimer();
                }
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error($"[Agent] 处理配置变更事件失败", ex);
            }
        }

        /// <summary>重建 ActionExecutor（工具/插件/动画执行器）</summary>
        private void RebuildActionExecutor()
        {
            _actionExecutor = new ActionExecutor(
                _toolService,
                mood => _moodManager.ChangeMoodByEvent(mood.ToString()),
                (tag, name) => _memoryCoordinator.ShortTermMemory.AddMessage(ChatRoles.System, $"{tag} {name}"),
                anim =>
                {
                    _eventQueue.LastEvent = $"animation:{anim}";
                    _eventDispatcher.Publish(new EventData
                    {
                        Category = EventCategory.MoodChange,
                        Trigger = EventTrigger.Tool,
                        Info = JsonSerializer.Serialize(new { animation = anim, source = EventSources.Tool })
                    });
                });
        }

        /// <summary>
        /// 根据权重概率选择子人格
        /// 所有权重和必须为100，否则返回第一个子人格（禁用切换机制）
        /// </summary>
        private SubPersonality? SelectSubPersonalityByWeight()
        {
            if (_personality?.Personalities == null || _personality.Personalities.Count == 0)
                return null;

            var totalWeight = _personality.Personalities.Sum(p => p.Weight);

            // 权重和不为100时，禁用切换机制，使用第一个子人格
            if (totalWeight != 100)
            {
                _configReader.Logger.Warn($"[Agent] 子人格权重和({totalWeight})不为100，人格切换机制已禁用，使用默认子人格");
                return _personality.Personalities[0];
            }

            // 按权重随机选择
            var roll = Random.Shared.Next(100);
            var cumulative = 0;
            foreach (var sub in _personality.Personalities)
            {
                cumulative += sub.Weight;
                if (roll < cumulative)
                {
                    _configReader.Logger.Info($"[Agent] 按权重选择子人格: {sub.Name} (权重: {sub.Weight}, 随机值: {roll})");
                    return sub;
                }
            }

            // fallback
            return _personality.Personalities[0];
        }

        /// <summary>创建对话模型 LlmClient（ChatModels）</summary>
        private LlmClient CreateChatLlmClient()
        {
            var (provider, model) = GetChatModel();
            _configReader.Logger.Info($"[Agent] 创建对话 LlmClient: Provider={provider}, Model={model}");
            return new LlmClient(provider, model, _configReader);
        }

        /// <summary>创建函数调用模型 LlmClient（FunctionModels，用于摘要、关键词提取等后台任务）</summary>
        private void ResolveFunctionModel()
        {
            var (provider, model) = GetFunctionModel();
            _functionProviderName = provider;
            _functionModelName = model;
            _configReader.Logger.Info($"[Agent] 解析函数调用模型: Provider={provider}, Model={model}");
        }

        // ========== 心情管理（委托给 MoodManager） ==========

        /// <summary>获取当前情绪</summary>
        public AgentMood CurrentMood => _moodManager.CurrentMood;

        // ========== 统一事件处理（IAgent 接口实现） ==========

        /// <summary>处理事件（入队，由 EventProcessingQueue 串行处理）</summary>
        public Task ProcessEventAsync(EventData eventData)
        {
            return _eventQueue.EnqueueEventAsync(eventData);
        }

        /// <summary>实际事件处理逻辑（由 EventProcessingQueue 回调）</summary>
        private async Task ProcessEventInternalAsync(EventData eventData)
        {
            _eventQueue.LastEvent = eventData.Category.ToString();

            try
            {
                // 内置任务过滤：活动记录 + 碎碎念短路 + 条件检查
                var filterResult = await _autoEventFilter.UpdateAsync(eventData);
                if (filterResult != AutoEventResult.Continue)
                    return;

                string userMessage;

                // 根据事件分类构建用户消息
                switch (eventData.Category)
                {
                    case EventCategory.UserInput:
                        userMessage = eventData.Info;
                        break;

                    case EventCategory.SystemAuto:
                        userMessage = PromptBuilder.BuildAutoEventPrompt(eventData);
                        break;

                    default:
                        return;
                }

                // ===== 视觉注入 =====
                // screenDescription 不混入 userMessage，避免进入记忆系统和聊天历史
                byte[]? screenshotBytes = null;
                string? screenDescription = null;
                var visionSettings = _configReader.GetModuleSettings();
                if (ScreenshotPolicy.ShouldCapture(eventData, visionSettings))
                {
                    if (_chatLlmClient.SupportsVision)
                    {
                        // 聊天模型支持视觉 → 直接截取图片，后续作为多模态消息发送
                        screenshotBytes = ScreenshotService.CaptureScreen(_configReader);
                        if (screenshotBytes != null)
                            _configReader.Logger.Debug("[Agent] 聊天模型支持视觉，将直传截图图片");
                    }
                    else
                    {
                        // 聊天模型不支持视觉 → 走 VisionService 文字转义
                        screenDescription = await _visionService.TryDescribeScreenAsync();
                    }
                }

                await ProcessWithLlmAsync(eventData, userMessage, screenDescription, screenshotBytes);

                // 更新情绪（根据用户交互内容和时间自动判断）
                if (eventData.Category == EventCategory.UserInput)
                {
                    var moodEvent = _moodManager.DetectMoodEvent(userMessage);
                    if (moodEvent != null)
                        _eventQueue.LastEvent = moodEvent;
                }
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error($"[Agent] ProcessEventInternalAsync 异常", ex);
                throw; // 重新抛出，让 EventProcessingQueue 处理状态转换
            }
        }

        /// <summary>工具调用循环最大迭代次数（防止无限循环）</summary>
        private const int MaxToolLoopIterations = 5;

        /// <summary>使用 LLM 处理事件（含工具调用循环）</summary>
        private async Task ProcessWithLlmAsync(EventData eventData, string userMessage, string? screenDescription = null, byte[]? imageBytes = null)
        {
            // 1. 记录原始用户消息到短期记忆（不含截图描述）
            _memoryCoordinator.ShortTermMemory.AddMessage(ChatRoles.User, userMessage);

            // 持久化原始用户消息到 SQLite
            _ = _chatHistoryRepo.SaveSingleMessageAsync(new ChatMessage
            {
                Role = ChatRoles.User,
                Content = userMessage,
                Timestamp = DateTime.Now
            });

            // 检查是否需要触发短期记忆总结
            await _memoryCoordinator.CheckAndSummarizeIfNeededAsync();

            // 如果有截图描述，拼接到送给 LLM 的消息中（不影响记忆和历史）
            var llmMessage = userMessage;
            if (!string.IsNullOrEmpty(screenDescription))
            {
                llmMessage = $"【用户屏幕画面】{screenDescription}\n\n{userMessage}";
            }

            // 2. 构建系统 Prompt（循环内不变）
            var systemPrompt = _promptBuilder.BuildSystemPrompt(
                _personality, _currentSubPersonality, _appSettings, _moodManager.CurrentMood);

            // 3. 工具调用循环：LLM 调用 → 执行 actions → 若有工具调用则带着结果继续循环
            var reply = string.Empty;
            var fallbackReply = string.Empty;
            var lastRawResponse = string.Empty;
            var hadToolCall = false;

            for (int iteration = 0; iteration < MaxToolLoopIterations; iteration++)
            {
                // 3a. 构建用户上下文（每次循环读取最新短期记忆，包含工具执行结果）
                var longTermStr = await _memoryCoordinator.RetrieveLongTermMemoryAsync(userMessage);
                var recentMessages = _memoryCoordinator.ShortTermMemory.GetRecentMessages(10);
                var shortTermStr = string.Join("\n", recentMessages.Select(m => $"[{m.Role}] {m.Content}"));
                var userContext = _promptBuilder.BuildUserContext(
                    llmMessage, longTermStr, shortTermStr, _lastJsonError);

                // 3b. 调用 LLM
                var messages = new List<ChatMessage>
                {
                    new() { Role = ChatRoles.System, Content = systemPrompt },
                    new() { Role = ChatRoles.User, Content = userContext }
                };

                // 转换为 OpenAI 消息格式
                var openAiMessages = messages.Select(m => m.Role switch
                {
                    ChatRoles.System => (OpenAiChatMessage)new OpenAiSystemChatMessage(m.Content),
                    ChatRoles.User => new OpenAiUserChatMessage(m.Content),
                    ChatRoles.Assistant => new OpenAiAssistantChatMessage(m.Content),
                    _ => new OpenAiUserChatMessage(m.Content)
                }).ToList();

                // 首次调用且有截图图片 → 多模态消息直传图片
                string response;
                if (iteration == 0 && imageBytes != null && imageBytes.Length > 0)
                    response = await _chatLlmClient.SendChatWithImageAsync(openAiMessages, imageBytes);
                else
                    response = await _chatLlmClient.SendChatAsync(openAiMessages);
                lastRawResponse = response;

                // 3c. 解析 LLM 响应
                var (parsedReply, actions) = ParseResponse(response);
                fallbackReply = parsedReply;

                // 3d. 执行 actions（工具结果自动记录到短期记忆）
                reply = await _actionExecutor.ExecuteActionsAsync(actions, _appSettings.MaxActionsPerResponse);

                // 3e. 判断是否还有工具调用（排除 reply/mood/animation/memory 等非工具类型）
                hadToolCall = actions?.Any(a =>
                    a.Type == ActionTypes.ToolCall ||
                    a.Type == ActionTypes.PluginCall ||
                    a.Type == ActionTypes.McpCall) ?? false;

                // 3f. 如果有 reply 或者没有工具调用，结束循环
                if (!string.IsNullOrEmpty(reply) || !hadToolCall)
                    break;

                _configReader.Logger.Debug($"[Agent] 工具调用循环第 {iteration + 1} 轮，继续调用 LLM");
            }

            // 调试：打印短期记忆内容到日志
            if (_appSettings.DebugLogToolResult)
            {
                var debugMessages = _memoryCoordinator.ShortTermMemory.GetRecentMessages(20);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[Debug] 短期记忆内容：");
                for (int i = 0; i < debugMessages.Count; i++)
                {
                    var m = debugMessages[i];
                    sb.AppendLine($"  [{i}] [{m.Role}] {m.Content}");
                }
                _configReader.Logger.Debug(sb.ToString());
            }

            // 4. 如果循环结束仍无 reply 但有 fallback（JSON解析失败时的原始文本），使用 fallback
            //    排除 fallback 等于原始 JSON 响应的情况（那不是真正的回复）
            if (string.IsNullOrEmpty(reply) && !string.IsNullOrEmpty(fallbackReply) && fallbackReply != lastRawResponse)
            {
                reply = fallbackReply;
            }

            // 5. 如果有回复，记录到短期记忆并发布回复事件
            if (!string.IsNullOrEmpty(reply))
            {
                _memoryCoordinator.ShortTermMemory.AddMessage(ChatRoles.Assistant, reply);

                // 持久化助手回复到 SQLite
                _ = _chatHistoryRepo.SaveSingleMessageAsync(new ChatMessage
                {
                    Role = ChatRoles.Assistant,
                    Content = reply,
                    Timestamp = DateTime.Now
                });

                // 发布回复事件，供 UI 订阅显示
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.ToolResult,
                    Trigger = EventTrigger.Llm,
                    Info = JsonSerializer.Serialize(new
                    {
                        type = Tools.Reply,
                        content = reply,
                        source = eventData.Category.ToString()
                    })
                });
            }
        }

        // ========== 状态查询 ==========

        public AgentStatus GetStatus()
        {
            return new AgentStatus
            {
                CurrentMood = _moodManager.CurrentMood.ToString(),
                ShortTermMemoryCount = _memoryCoordinator.ShortTermMemory.Count,
                MidTermMemoryCount = 0,
                LongTermMemoryCount = _memoryCoordinator.LongMemoryCount,
                IsProcessing = _eventQueue.IsProcessing,
                LastEvent = _eventQueue.LastEvent,
                State = _eventQueue.State
            };
        }

        // ========== 私有方法 ==========

        /// <summary>从模型名中提取提供商（格式："{提供商}/{模型名}"）</summary>
        private static (string provider, string model) ParseModelName(string modelFullName)
        {
            if (string.IsNullOrEmpty(modelFullName) || modelFullName == "default")
                return (ProviderConfig.DefaultProviderName, "default");

            var parts = modelFullName.Split('/', 2);
            if (parts.Length == 2)
                return (parts[0], parts[1]);

            return (ProviderConfig.DefaultProviderName, modelFullName);
        }

        /// <summary>获取对话模型名称（从主人格读取 ChatModels）</summary>
        private (string provider, string model) GetChatModel()
        {
            // 从主人格获取模型
            if (_personality?.ChatModels?.Count > 0)
            {
                return ParseModelName(_personality.ChatModels[0]);
            }

            return (ProviderConfig.DefaultProviderName, "default");
        }

        /// <summary>获取函数调用模型名称（从主人格读取 FunctionModels）</summary>
        private (string provider, string model) GetFunctionModel()
        {
            // 从主人格获取函数调用模型
            if (_personality?.FunctionModels?.Count > 0)
            {
                return ParseModelName(_personality.FunctionModels[0]);
            }

            // 如果没有配置 FunctionModels，回退到 ChatModels
            return GetChatModel();
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

        // ========== 资源释放 ==========

        public void Dispose()
        {
            _memoryCoordinator.Dispose();
            _eventQueue.Dispose();
            GC.SuppressFinalize(this);
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
