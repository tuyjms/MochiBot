using System.Collections.Concurrent;
using System.Text.Json;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Core.Database;
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
        private IShortTermMemory _shortTermMemory;
        private ILongMemory _longMemory;
        private readonly IToolService _toolService;
        private readonly MoodLogRepository? _moodLogRepository;
        private AppSettings _appSettings;
        private PersonalityConfig? _personality;
        private SubPersonality? _currentSubPersonality;

        // 事件订阅ID
        private readonly List<string> _subscriptionIds = new();

        // ========== 心情记录器（集成到 Agent 内部） ==========
        private AgentMood _currentMood = AgentMood.Neutral;

        private ActionExecutor _actionExecutor;
        private readonly PromptFormatter _systemPromptFormatter;
        private readonly PromptFormatter _userContextFormatter;
        private bool _isProcessing;
        private string _lastEvent = string.Empty;
        private string _lastJsonError = string.Empty;

        // ========== 用户活动跟踪（用于用眼提醒/空闲检测条件判断） ==========
        private DateTime _lastActivityTime = DateTime.Now;

        // ========== 长期记忆维护 ==========
        private Timer? _maintenanceTimer;
        private int _longMemoryCount = 0;

        // ========== 事件队列 + 状态机 ==========
        private readonly ConcurrentQueue<EventData> _eventQueue = new();
        private const int MaxQueueSize = 20;
        private volatile AgentState _state = AgentState.Idle;
        private readonly SemaphoreSlim _processLock = new(1, 1);
        private string _functionProviderName = string.Empty;
        private string _functionModelName = string.Empty;

        // 自动事件 LLM 提示词（BuildAutoEventPrompt 使用）
        private const string MurmurPrompt = "你现在想对用户说一句碎碎念/撒娇的话，表达你的思念或关心。";
        private const string EyeRestPrompt = "用户已经盯着屏幕很久了，提醒他休息一下眼睛。";
        private const string LateNightPrompt = "已经很晚了，关心用户为什么还没睡，温柔地催他睡觉。";
        private const string IdleCheckPrompt = "用户已经离开一段时间了，说一句想念的话或者自言自语。";
        private const string DefaultEventFallback = "请根据事件生成合适的回复。";

        // 短期记忆中的系统消息标签
        private const string TagMidTermMemory = "[中期记忆]";
        private const string TagToolExecution = "[工具执行]";
        private const string TagPluginExecution = "[插件执行]";
        private const string TagMcpExecution = "[MCP执行]";

        // 对话模式 System Prompt 模板（固定模板，人格提示词动态注入）
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
【长期记忆】{LongTermMemory}

【短期记忆】{ShortTermMemory}

用户说：{UserMessage}

请通过 actions 中的 reply 工具来回复用户，不要直接在 reply 字段中写回复。
";

        public MainAgent(
            IEventDispatcher eventDispatcher,
            IConfigReader configReader,
            IToolService toolService,
            MoodLogRepository? moodLogRepository = null)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
            _moodLogRepository = moodLogRepository;

            _appSettings = configReader.GetAppSettings();
            _personality = configReader.GetActivePersonality();

            // 选择当前子人格（按权重概率）
            _currentSubPersonality = SelectSubPersonalityByWeight();

            // 自创建 LlmClient（对话模型）
            _chatLlmClient = CreateChatLlmClient();
            ResolveFunctionModel();

            // 自创建 ShortTermMemory（使用函数调用模型，自维护LlmClient）
            var maxMessages = _personality?.MaxMessages ?? 50;
            _shortTermMemory = new ShortTermMemory(maxMessages, _functionProviderName, _functionModelName, _configReader);

            // 应用溢出策略配置
            var strategyStr = _configReader.GetModuleSettings().ShortTermMemory_OverflowStrategy;
            if (Enum.TryParse<OverflowStrategy>(strategyStr, true, out var strategy))
                _shortTermMemory.OverflowStrategy = strategy;

            // 自创建 LongMemory（使用函数调用模型，自维护LlmClient）
            _longMemory = new LongMemory(_functionProviderName, _functionModelName, _configReader);

            // 创建 ActionExecutor，将 actions 执行逻辑委托给它
            _actionExecutor = new ActionExecutor(
                _toolService,
                mood => ChangeMoodByEvent(mood.ToString()),
                (desc, param) => _shortTermMemory.AddMessage(ChatRoles.System, $"{TagMidTermMemory} {desc}"),
                anim =>
                {
                    _lastEvent = $"animation:{anim}";
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

            _systemPromptFormatter = new PromptFormatter(SystemPromptTemplate);
            _userContextFormatter = new PromptFormatter(UserContextTemplate);

            // 订阅事件调度器
            SubscribeToEvents();

            // 注册模块状态
            _eventDispatcher.RegisterModule("agent", AgentState.Idle.ToString().ToLower());

            // 启动长期记忆维护定时器
            var promotionInterval = _configReader.GetModuleSettings().LongTermMemory_PromotionInterval;
            _maintenanceTimer = new Timer(async _ => await RunMemoryMaintenanceAsync(),
                null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(promotionInterval));

            // 初始化长期记忆计数
            _ = InitializeLongMemoryCountAsync();
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
                        if (uiType == UiInteractionTypes.Pet)
                        {
                            _lastEvent = Pet;
                            ChangeMoodByEvent(Pet);
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

                    var maxMessages = _personality?.MaxMessages ?? 50;
                    _shortTermMemory = new ShortTermMemory(maxMessages, _functionProviderName, _functionModelName, _configReader);
                    _longMemory = new LongMemory(_functionProviderName, _functionModelName, _configReader);

                    _actionExecutor = new ActionExecutor(
                        _toolService,
                        mood => ChangeMoodByEvent(mood.ToString()),
                        (desc, param) => _shortTermMemory.AddMessage(ChatRoles.System, $"{TagMidTermMemory} {desc}"),
                        anim =>
                        {
                            _lastEvent = $"animation:{anim}";
                            _eventDispatcher.Publish(new EventData
                            {
                                Category = EventCategory.MoodChange,
                                Trigger = EventTrigger.Tool,
                                Info = JsonSerializer.Serialize(new { animation = anim, source = EventSources.Tool })
                            });
                        });

                    _configReader.Logger.Info("[Agent] ProviderConfig 已变更，所有 LlmClient 和记忆模块已重建");
                }

                // 检查人格配置是否变更（刷新人格描述）
                if (items.Contains("PersonalityConfig"))
                {
                    _personality = _configReader.GetActivePersonality();
                    _currentSubPersonality = SelectSubPersonalityByWeight();
                    _configReader.Logger.Info("[Agent] 人格配置已刷新");
                }

                // 检查 MaxMessages 是否变更
                if (items.Contains("MaxMessages") && _personality != null)
                {
                    _shortTermMemory.Capacity = _personality.MaxMessages;
                    _configReader.Logger.Info($"[Agent] 短期记忆容量已调整为: {_personality.MaxMessages}");
                }
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error($"[Agent] 处理配置变更事件失败", ex);
            }
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

        // ========== 心情记录器（集成到 Agent 内部） ==========

        /// <summary>获取当前情绪</summary>
        public AgentMood CurrentMood => _currentMood;

        /// <summary>根据事件类型切换心情，并通过事件调度器发布 MoodChange 事件</summary>
        private void ChangeMoodByEvent(string eventType)
        {
            var newMood = eventType switch
            {
                LateNight or Sleepy => AgentMood.Sleepy,
                LongWork => AgentMood.Neutral,
                Idle => AgentMood.Sad,
                Active => AgentMood.Neutral,
                Pet => AgentMood.Touched,
                Compliment => AgentMood.Happy,
                Angry => AgentMood.Angry,
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
            if (_moodLogRepository != null)
            {
                _ = _moodLogRepository.LogMoodChangeAsync(newMood, eventType);
            }
        }

        // ========== 统一事件处理（入队 + 触发处理循环） ==========

        public Task ProcessEventAsync(EventData eventData)
        {
            // 队列满时丢弃最旧事件
            while (_eventQueue.Count >= MaxQueueSize)
            {
                _eventQueue.TryDequeue(out _);
                _configReader.Logger.Warn("[Agent] 事件队列已满，丢弃最旧事件");
            }

            _eventQueue.Enqueue(eventData);
            _configReader.Logger.Debug($"[Agent] 事件已入队: {eventData.Category}, 队列长度: {_eventQueue.Count}");

            TryStartProcessing();
            return Task.CompletedTask;
        }

        // ========== 状态机 ==========

        /// <summary>尝试启动处理循环（仅 Idle 状态可启动）</summary>
        private void TryStartProcessing()
        {
            if (_state != AgentState.Idle) return;
            if (!_processLock.Wait(0)) return;

            _ = ProcessQueueAsync();
        }

        /// <summary>事件处理循环：从队列逐个取出事件串行处理</summary>
        private async Task ProcessQueueAsync()
        {
            try
            {
                while (_eventQueue.TryDequeue(out var eventData))
                {
                    SetState(AgentState.Thinking);
                    try
                    {
                        await ProcessEventInternalAsync(eventData);
                    }
                    catch (Exception ex)
                    {
                        _configReader.Logger.Error($"[Agent] 处理事件异常: {eventData.Category}", ex);
                        SetState(AgentState.Error);
                        await Task.Delay(1000); // 错误冷却
                    }
                    finally
                    {
                        SetState(AgentState.Idle);
                    }
                }
            }
            finally
            {
                _processLock.Release();
            }
        }

        /// <summary>设置 Agent 状态并上报到 EventDispatcher</summary>
        private void SetState(AgentState newState)
        {
            if (_state == newState) return;
            _state = newState;
            _configReader.Logger.Debug($"[Agent] 状态: {newState}");
            _eventDispatcher.UpdateModuleState("agent", newState.ToString().ToLower());
        }

        /// <summary>实际事件处理逻辑（从原 ProcessEventAsync 移入）</summary>
        private async Task ProcessEventInternalAsync(EventData eventData)
        {
            _isProcessing = true;
            _lastEvent = eventData.Category.ToString();

            try
            {
                // 用户输入时记录活动时间
                if (eventData.Category == EventCategory.UserInput)
                {
                    _lastActivityTime = DateTime.Now;
                }

                // 检查是否为碎碎念事件，根据权重决定使用内置文本还是 LLM
                if (eventData.Category == EventCategory.SystemAuto && TryHandleMurmur(eventData))
                    return;

                // 系统自动事件：条件检查，不满足则跳过（避免无效 LLM 调用）
                if (eventData.Category == EventCategory.SystemAuto && !ShouldProcessEvent(eventData))
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
                if (type != BuiltinTasks.Murmur)
                    return false;

                // 从 parameters 中读取权重
                int weight = 30;
                if (doc.RootElement.TryGetProperty("parameters", out var paramsProp))
                {
                    int.TryParse(paramsProp.GetString(), out weight);
                }

                // 根据权重随机决定：roll < weight 时使用 LLM，否则使用内置文本
                var roll = Random.Shared.Next(100);
                if (roll < weight)
                {
                    // 使用 LLM 生成回复（返回 false 让 ProcessEventAsync 继续处理）
                    return false;
                }

                // 使用内置碎碎念文本（通过 ToolService 统一工具接口）
                var result = _toolService.ExecuteToolAsync(Tools.Murmur, "{}").GetAwaiter().GetResult();
                if (result.Success)
                {
                    using var resultDoc = JsonDocument.Parse(result.Data);
                    var text = resultDoc.RootElement.TryGetProperty(Tools.Murmur, out var murmurProp)
                        ? murmurProp.GetString() ?? ""
                        : "";

                    if (!string.IsNullOrEmpty(text))
                    {
                        _shortTermMemory.AddMessage(ChatRoles.Assistant, text);
                        _eventDispatcher.Publish(new EventData
                        {
                            Category = EventCategory.ToolResult,
                            Trigger = EventTrigger.System,
                            Info = JsonSerializer.Serialize(new
                            {
                                type = Tools.Reply,
                                content = text,
                                source = Tools.Murmur
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

        /// <summary>
        /// 系统自动事件条件检查
        /// 检查用眼提醒和空闲检测的前置条件，不满足则跳过 LLM 调用
        /// </summary>
        private bool ShouldProcessEvent(EventData eventData)
        {
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                    return true;

                var type = typeProp.GetString();

                // 用眼提醒：检查是否达到阈值（默认 120 分钟）
                if (type == BuiltinTasks.EyeRest)
                {
                    int threshold = 120;
                    if (doc.RootElement.TryGetProperty("parameters", out var p) && int.TryParse(p.GetString(), out var t))
                        threshold = t;

                    var elapsed = (DateTime.Now - _lastActivityTime).TotalMinutes;
                    if (elapsed < threshold)
                        return false;
                }

                // 空闲检测：检查是否达到阈值（默认 5 分钟）
                if (type == BuiltinTasks.IdleCheck)
                {
                    int threshold = 5;
                    if (doc.RootElement.TryGetProperty("parameters", out var p) && int.TryParse(p.GetString(), out var t))
                        threshold = t;

                    var idleMinutes = (DateTime.Now - _lastActivityTime).TotalMinutes;
                    if (idleMinutes < threshold)
                        return false;
                }
            }
            catch { }

            return true;
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
            _shortTermMemory.AddMessage(ChatRoles.User, userMessage);

            // 检查是否需要触发短期记忆总结
            if (_shortTermMemory.IsSummarizePending)
            {
                // 先触发长期记忆录入（在短期记忆被压缩前）
                await _longMemory.SummarizeShortTermAsync(_shortTermMemory);
                // 更新长期记忆计数
                _longMemoryCount = await _longMemory.GetCountAsync();
                // 再压缩短期记忆
                await _shortTermMemory.SummarizeAsync();
            }

            // 2. 构建完整 Prompt
            var systemPrompt = BuildSystemPrompt();
            var userContext = await BuildUserContextAsync(userMessage);

            // 3. 调用 LLM（对话模式）
            var messages = new List<ChatMessage>
            {
                new() { Role = ChatRoles.System, Content = systemPrompt },
                new() { Role = ChatRoles.User, Content = userContext }
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
                _shortTermMemory.AddMessage(ChatRoles.Assistant, reply);

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
                        BuiltinTasks.Murmur => MurmurPrompt,
                        BuiltinTasks.EyeRest => EyeRestPrompt,
                        BuiltinTasks.LateNight => LateNightPrompt,
                        BuiltinTasks.IdleCheck => IdleCheckPrompt,
                        _ => $"事件类型：{type}。请根据这个事件生成合适的回复。"
                    };
                }
            }
            catch { }

            return DefaultEventFallback;
        }

        // ========== 长期记忆维护 ==========

        /// <summary>定期维护长期记忆（晋升高频访问记忆、淘汰低重要度长期未访问记忆）</summary>
        private async Task RunMemoryMaintenanceAsync()
        {
            try
            {
                var ms = _configReader.GetModuleSettings();
                await _longMemory.PromoteEntriesAsync(ms.LongTermMemory_PromotionThreshold, 10);
                await _longMemory.EvictEntriesAsync(0, 30);
                _longMemoryCount = await _longMemory.GetCountAsync();
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[Agent] 记忆维护失败: {ex.Message}");
            }
        }

        /// <summary>初始化长期记忆计数</summary>
        private async Task InitializeLongMemoryCountAsync()
        {
            try
            {
                _longMemoryCount = await _longMemory.GetCountAsync();
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[Agent] 初始化长期记忆计数失败: {ex.Message}");
            }
        }

        // ========== 状态查询 ==========

        public AgentStatus GetStatus()
        {
            return new AgentStatus
            {
                CurrentMood = _currentMood.ToString(),
                ShortTermMemoryCount = _shortTermMemory.Count,
                MidTermMemoryCount = 0,
                LongTermMemoryCount = _longMemoryCount,
                IsProcessing = _isProcessing,
                LastEvent = _lastEvent,
                State = _state
            };
        }

        // ========== 私有方法 ==========

        /// <summary>构建 System Prompt（人格提示词动态注入）</summary>
        private string BuildSystemPrompt()
        {
            var name = _personality?.Name ?? CharacterDefaults.DefaultName;
            var userName = _appSettings.UserName;

            // 人格描述：优先使用当前子人格的描述，否则使用人格根描述
            var personalityDesc = _currentSubPersonality?.Description
                ?? _personality?.Description
                ?? CharacterDefaults.DefaultDescription;

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
        private async Task<string> BuildUserContextAsync(string userMessage)
        {
            // 长期记忆检索
            var longTermStr = await RetrieveLongTermMemoryAsync(userMessage);

            // 短期记忆
            var recentMessages = _shortTermMemory.GetRecentMessages(10);
            var shortTermStr = string.Join("\n", recentMessages.Select(m => $"[{m.Role}] {m.Content}"));

            var result = _userContextFormatter.Format(new Dictionary<string, string>
            {
                { "LongTermMemory", longTermStr },
                { "ShortTermMemory", shortTermStr },
                { "UserMessage", userMessage }
            });

            // 如果有最近一次 JSON 解析错误，追加到 Prompt 末尾提醒 LLM
            if (!string.IsNullOrEmpty(_lastJsonError))
            {
                result += $"\n\n{_lastJsonError}";
            }

            return result;
        }

        /// <summary>从用户消息中提取关键词并检索长期记忆</summary>
        private async Task<string> RetrieveLongTermMemoryAsync(string userMessage)
        {
            try
            {
                var keywords = ExtractKeywords(userMessage);
                if (keywords.Count == 0)
                    return "（无）";

                var allResults = new List<LongMemoryEntry>();
                foreach (var keyword in keywords)
                {
                    var results = await _longMemory.SearchByKeywordsAsync(keyword);
                    allResults.AddRange(results);
                }

                // 去重（按 Id）
                var distinctResults = allResults
                    .GroupBy(e => e.Id)
                    .Select(g => g.First())
                    .OrderByDescending(e => e.Importance)
                    .Take(5)
                    .ToList();

                if (distinctResults.Count == 0)
                    return "（无）";

                // 更新访问计数
                foreach (var entry in distinctResults)
                {
                    await _longMemory.UpdateAccessAsync(entry.Id);
                }

                _configReader.Logger.Info($"[Agent] 检索到 {distinctResults.Count} 条长期记忆");

                return string.Join("\n", distinctResults.Select(e =>
                    $"[{e.EventTimestamp:yyyy-MM-dd}] {e.Description}"));
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[Agent] 长期记忆检索失败: {ex.Message}");
                return "（无）";
            }
        }

        /// <summary>简单关键词提取（按标点/空格分词，取前3个长度>=2的非停用词）</summary>
        private static List<string> ExtractKeywords(string text, int maxKeywords = 3)
        {
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "的", "了", "是", "在", "我", "你", "他", "她", "它",
                "这", "那", "有", "和", "与", "对", "把", "被", "让",
                "吗", "呢", "吧", "啊", "哦", "嗯", "好", "不", "没",
                "a", "an", "the", "is", "are", "was", "were", "i", "you",
                "he", "she", "it", "we", "they", "this", "that", "and", "or"
            };

            var words = text.Split(
                new[] { ' ', ',', '.', '!', '?', '。', '！', '？', '，', '、', '；', '：', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            return words
                .Where(w => w.Length >= 2 && !stopWords.Contains(w))
                .Take(maxKeywords)
                .ToList();
        }

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

        /// <summary>调用 LLM 对话模式</summary>
        private async Task<string> CallLlmChatAsync(List<ChatMessage> messages)
        {
            var openAiMessages = messages.Select(m => m.Role switch
            {
                ChatRoles.System => (OpenAiChatMessage)new OpenAiSystemChatMessage(m.Content),
                ChatRoles.User => new OpenAiUserChatMessage(m.Content),
                ChatRoles.Assistant => new OpenAiAssistantChatMessage(m.Content),
                _ => new OpenAiUserChatMessage(m.Content)
            }).ToList();

            return await _chatLlmClient.SendChatAsync(openAiMessages);
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
                    if (action.Type == ActionTypes.ToolCall && action.Name != Tools.Reply)
                    {
                        _shortTermMemory.AddMessage(ChatRoles.System, $"{TagToolExecution} {action.Name}");
                    }
                    else if (action.Type == ActionTypes.PluginCall)
                    {
                        _shortTermMemory.AddMessage(ChatRoles.System, $"{TagPluginExecution} {action.Name}");
                    }
                    else if (action.Type == ActionTypes.McpCall)
                    {
                        _shortTermMemory.AddMessage(ChatRoles.System, $"{TagMcpExecution} {action.ServerName}/{action.Name}");
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
                _lastEvent = LateNight;
                ChangeMoodByEvent(LateNight);
                return;
            }

            var msg = userMessage.ToLowerInvariant();

            if (msg.Contains("摸摸") || msg.Contains("摸头") || msg.Contains("拍头") || msg.Contains("抱抱"))
            {
                _lastEvent = Pet;
                ChangeMoodByEvent(Pet);
                return;
            }

            if (msg.Contains("夸") || msg.Contains("好看") || msg.Contains("可爱") || msg.Contains("漂亮") ||
                msg.Contains("喜欢你") || msg.Contains("真棒") || msg.Contains("厉害"))
            {
                _lastEvent = Compliment;
                ChangeMoodByEvent(Compliment);
                return;
            }

            _lastEvent = Active;
            ChangeMoodByEvent(Active);
        }

        // ========== 资源释放 ==========

        public void Dispose()
        {
            _maintenanceTimer?.Dispose();
            _processLock.Dispose();
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
