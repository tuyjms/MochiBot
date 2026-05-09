using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Database.Models;
using MochiBot.Src.Services;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 长期记忆模块实现
    /// 基于 LongMemoryRepository 进行数据持久化，支持关键词检索、重要度筛选、晋升和淘汰机制
    /// </summary>
    public class LongMemory : ILongMemory
    {
        private readonly LongMemoryRepository _repository;
        private readonly IConfigReader _configReader;
        private LlmClient? _llmClient;
        private readonly Random _random = new();

        public LongMemory(IConfigReader configReader, IDatabaseService? databaseService = null)
        {
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));

            var dbService = databaseService ?? new DatabaseService();
            _repository = new LongMemoryRepository(dbService);
            _repository.InitializeTable();
        }

        /// <summary>
        /// 可注入自定义连接字符串的构造函数（用于单元测试）
        /// </summary>
        public LongMemory(string connectionString, IConfigReader configReader)
        {
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            var dbService = new DatabaseService(connectionString);
            _repository = new LongMemoryRepository(dbService);
            _repository.InitializeTable();
        }

        /// <summary>设置或更新 LlmClient 实例（用于热重载时重建）</summary>
        public void SetLlmClient(LlmClient llmClient)
        {
            _llmClient = llmClient;
        }

        public async Task AddEntryAsync(LongMemoryEntry entry)
        {
            // 检查是否超过最大条目数
            var count = await _repository.GetCountAsync();
            var maxEntries = _configReader.GetModuleSettings().LongTermMemory_MaxEntries;
            if (count >= maxEntries)
            {
                // 淘汰最不重要的条目
                await EvictEntriesAsync(0, 90);
            }

            var model = new LongMemoryEntryModel
            {
                Id = entry.Id,
                Keyword1 = entry.Keyword1,
                Keyword2 = entry.Keyword2,
                Keyword3 = entry.Keyword3,
                Description = entry.Description,
                EventTimestamp = entry.EventTimestamp.ToString("O"),
                Importance = entry.Importance,
                CreatedAt = entry.CreatedAt.ToString("O"),
                LastAccessedAt = entry.LastAccessedAt.ToString("O"),
                AccessCount = entry.AccessCount
            };

            await _repository.AddEntryAsync(model);
        }

        public async Task<List<LongMemoryEntry>> SearchByKeywordsAsync(string keyword)
        {
            var searchTopN = _configReader.GetModuleSettings().LongTermMemory_SearchTopN;
            var models = await _repository.SearchByKeywordsAsync(keyword, searchTopN);
            return models.Select(MapModelToEntry).ToList();
        }

        public async Task<List<LongMemoryEntry>> GetByImportanceAsync(int minImportance)
        {
            var models = await _repository.GetByImportanceAsync(minImportance);
            return models.Select(MapModelToEntry).ToList();
        }

        public async Task<List<LongMemoryEntry>> GetByTimeRangeAsync(DateTime start, DateTime end)
        {
            var models = await _repository.GetByTimeRangeAsync(start, end);
            return models.Select(MapModelToEntry).ToList();
        }

        public async Task UpdateAccessAsync(string entryId)
        {
            await _repository.UpdateAccessAsync(entryId);
        }

        public async Task DeleteEntryAsync(string entryId)
        {
            await _repository.DeleteEntryAsync(entryId);
        }

        public async Task ClearAllAsync()
        {
            await _repository.ClearAllAsync();
        }

        public async Task<int> GetCountAsync()
        {
            return await _repository.GetCountAsync();
        }

        public async Task PromoteEntriesAsync(int accessThreshold, int importanceIncrement)
        {
            var affected = await _repository.PromoteEntriesAsync(accessThreshold, importanceIncrement);
            if (affected > 0)
            {
                _configReader.Logger.Info($"[LongMemory] 晋升了 {affected} 条长期记忆 (访问阈值: {accessThreshold}, 增量: {importanceIncrement})");
            }
        }

        public async Task EvictEntriesAsync(int minImportance, int maxInactiveDays)
        {
            var affected = await _repository.EvictEntriesAsync(minImportance, maxInactiveDays);
            if (affected > 0)
            {
                _configReader.Logger.Info($"[LongMemory] 淘汰了 {affected} 条长期记忆 (重要度<{minImportance}, 未访问>{maxInactiveDays}天)");
            }
        }

        /// <summary>
        /// 使用 LLM 评估一段文本的重要度（0-100）
        /// </summary>
        public async Task<int> EvaluateImportanceAsync(string text)
        {
            if (_llmClient == null)
                return _random.Next(20, 60); // 没有 LlmClient 时随机返回

            try
            {
                var prompt = $"请评估以下信息的重要程度（0-100分），只返回数字：\n\n{text}";
                var response = await _llmClient.SendChatAsync(prompt);

                if (int.TryParse(response.Trim(), out var importance))
                {
                    return Math.Clamp(importance, 0, 100);
                }

                return 50;
            }
            catch
            {
                return _random.Next(20, 60);
            }
        }

        /// <summary>
        /// 使用 LLM 从文本中提取关键词（最多3个）
        /// </summary>
        public async Task<(string kw1, string kw2, string kw3)> ExtractKeywordsAsync(string text)
        {
            if (_llmClient == null)
            {
                // 没有 LlmClient 时简单提取前几个词
                var words = text.Split(new[] { ' ', '，', '。', '！', '？', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return (
                    words.Length > 0 ? words[0] : "general",
                    words.Length > 1 ? words[1] : "general",
                    words.Length > 2 ? words[2] : "general"
                );
            }

            try
            {
                var prompt = $"从以下文本中提取3个关键词，用逗号分隔，只返回关键词：\n\n{text}";
                var response = await _llmClient.SendChatAsync(prompt);

                var parts = response.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return (
                    parts.Length > 0 ? parts[0] : "general",
                    parts.Length > 1 ? parts[1] : "general",
                    parts.Length > 2 ? parts[2] : "general"
                );
            }
            catch
            {
                return ("general", "general", "general");
            }
        }

        private static LongMemoryEntry MapModelToEntry(LongMemoryEntryModel model)
        {
            return new LongMemoryEntry
            {
                Id = model.Id,
                Keyword1 = model.Keyword1,
                Keyword2 = model.Keyword2,
                Keyword3 = model.Keyword3,
                Description = model.Description,
                EventTimestamp = DateTime.Parse(model.EventTimestamp),
                Importance = model.Importance,
                CreatedAt = DateTime.Parse(model.CreatedAt),
                LastAccessedAt = DateTime.Parse(model.LastAccessedAt),
                AccessCount = model.AccessCount
            };
        }
    }
}
