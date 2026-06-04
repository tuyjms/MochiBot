using MochiBot.Src.Core.Config;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 记忆模块协调器
    /// 负责短期/长期记忆的创建、维护、总结触发、检索和配置热重载
    /// </summary>
    public class MemoryCoordinator : IDisposable
    {
        private readonly IConfigReader _configReader;
        private readonly ChatHistoryRepository? _chatHistoryRepo;

        /// <summary>短期记忆实例</summary>
        public IShortTermMemory ShortTermMemory { get; private set; }

        /// <summary>长期记忆实例</summary>
        public ILongMemory LongMemory { get; private set; }

        /// <summary>长期记忆条目数</summary>
        public int LongMemoryCount { get; private set; }

        private Timer? _maintenanceTimer;

        /// <summary>
        /// 创建记忆协调器
        /// </summary>
        /// <param name="functionProvider">函数调用模型提供商</param>
        /// <param name="functionModel">函数调用模型名</param>
        /// <param name="configReader">配置读取器</param>
        public MemoryCoordinator(string functionProvider, string functionModel, IConfigReader configReader, ChatHistoryRepository? chatHistoryRepo = null)
        {
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _chatHistoryRepo = chatHistoryRepo;

            ShortTermMemory = CreateShortTermMemory(functionProvider, functionModel);
            LongMemory = new LongMemory(functionProvider, functionModel, configReader);
        }

        /// <summary>启动长期记忆维护定时器</summary>
        public void StartMaintenanceTimer()
        {
            var promotionInterval = _configReader.GetModuleSettings().LongTermMemory_PromotionInterval;
            _maintenanceTimer = new Timer(
                async _ => await RunMemoryMaintenanceAsync(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(promotionInterval));

            // 初始化长期记忆计数
            _ = InitializeCountAsync();

            // 从数据库预热短期记忆
            _ = WarmUpFromDatabaseAsync();
        }

        /// <summary>
        /// 检查短期记忆是否需要总结，如需要则触发长期记忆录入 + 短期记忆压缩
        /// </summary>
        public async Task CheckAndSummarizeIfNeededAsync()
        {
            if (!ShortTermMemory.IsSummarizePending)
                return;

            // 先触发长期记忆录入（在短期记忆被压缩前）
            await LongMemory.SummarizeShortTermAsync(ShortTermMemory);
            // 更新长期记忆计数
            LongMemoryCount = await LongMemory.GetCountAsync();
            // 再压缩短期记忆
            await ShortTermMemory.SummarizeAsync();
        }

        /// <summary>
        /// 从用户消息中提取关键词并检索长期记忆
        /// </summary>
        /// <param name="userMessage">用户消息</param>
        /// <returns>格式化的长期记忆字符串，无结果时返回"（无）"</returns>
        public async Task<string> RetrieveLongTermMemoryAsync(string userMessage)
        {
            try
            {
                var keywords = ExtractKeywords(userMessage);
                if (keywords.Count == 0)
                    return "（无）";

                var allResults = new List<LongMemoryEntry>();
                foreach (var keyword in keywords)
                {
                    var results = await LongMemory.SearchByKeywordsAsync(keyword);
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
                    await LongMemory.UpdateAccessAsync(entry.Id);
                }

                _configReader.Logger.Info($"[Memory] 检索到 {distinctResults.Count} 条长期记忆");

                return string.Join("\n", distinctResults.Select(e =>
                    $"[{e.EventTimestamp:yyyy-MM-dd}] {e.Description}"));
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[Memory] 长期记忆检索失败: {ex.Message}");
                return "（无）";
            }
        }

        /// <summary>
        /// 配置热重载：重建所有记忆实例
        /// </summary>
        /// <param name="functionProvider">新的函数调用模型提供商</param>
        /// <param name="functionModel">新的函数调用模型名</param>
        public void RebuildMemories(string functionProvider, string functionModel)
        {
            ShortTermMemory = CreateShortTermMemory(functionProvider, functionModel);
            LongMemory = new LongMemory(functionProvider, functionModel, _configReader);
            _configReader.Logger.Info("[Memory] ProviderConfig 已变更，记忆模块已重建");

            // 重建后重新预热
            _ = WarmUpFromDatabaseAsync();
        }

        /// <summary>
        /// 更新短期记忆容量
        /// </summary>
        public void UpdateCapacity(int capacity)
        {
            ShortTermMemory.Capacity = capacity;
            _configReader.Logger.Info($"[Memory] 短期记忆容量已调整为: {capacity}");
        }

        /// <summary>从数据库预热短期记忆（启动时加载最近的聊天记录）</summary>
        private async Task WarmUpFromDatabaseAsync()
        {
            if (_chatHistoryRepo == null) return;

            try
            {
                var history = await _chatHistoryRepo.LoadChatHistoryAsync(limit: ShortTermMemory.Capacity);
                if (history.Count == 0) return;

                foreach (var msg in history)
                {
                    ShortTermMemory.AddMessage(msg.Role, msg.Content);
                }

                _configReader.Logger.Info($"[Memory] 从数据库预热短期记忆: {history.Count} 条消息");
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[Memory] 从数据库预热短期记忆失败: {ex.Message}");
            }
        }

        // ========== 私有方法 ==========

        private IShortTermMemory CreateShortTermMemory(string functionProvider, string functionModel)
        {
            var personality = _configReader.GetActivePersonality();
            var maxMessages = personality?.MaxMessages ?? 50;
            var memory = new ShortTermMemory(maxMessages, functionProvider, functionModel, _configReader);

            // 应用溢出策略配置
            var strategyStr = _configReader.GetModuleSettings().ShortTermMemory_OverflowStrategy;
            if (Enum.TryParse<OverflowStrategy>(strategyStr, true, out var strategy))
                memory.OverflowStrategy = strategy;

            return memory;
        }

        private async Task RunMemoryMaintenanceAsync()
        {
            try
            {
                var ms = _configReader.GetModuleSettings();
                await LongMemory.PromoteEntriesAsync(ms.LongTermMemory_PromotionThreshold, 10);
                await LongMemory.EvictEntriesAsync(0, 30);
                LongMemoryCount = await LongMemory.GetCountAsync();
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[Memory] 记忆维护失败: {ex.Message}");
            }
        }

        private async Task InitializeCountAsync()
        {
            try
            {
                LongMemoryCount = await LongMemory.GetCountAsync();
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[Memory] 初始化长期记忆计数失败: {ex.Message}");
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

        public void Dispose()
        {
            _maintenanceTimer?.Dispose();
        }
    }
}
