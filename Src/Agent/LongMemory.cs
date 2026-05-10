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
        private readonly LlmClient _llmClient;
        private readonly Random _random = new();

        public LongMemory(string provider, string model, IConfigReader configReader, IDatabaseService? databaseService = null)
        {
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _llmClient = new LlmClient(provider, model, configReader);

            var dbService = databaseService ?? new DatabaseService();
            _repository = new LongMemoryRepository(dbService);
            _repository.InitializeTable();
        }

        /// <summary>
        /// 可注入自定义连接字符串的构造函数（用于单元测试）
        /// </summary>
        public LongMemory(string connectionString, string provider, string model, IConfigReader configReader)
        {
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _llmClient = new LlmClient(provider, model, configReader);
            var dbService = new DatabaseService(connectionString);
            _repository = new LongMemoryRepository(dbService);
            _repository.InitializeTable();
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

        public async Task SummarizeShortTermAsync(IShortTermMemory shortTermMemory)
        {
            var messages = shortTermMemory.GetAllMessages();
            if (messages.Count == 0) return;

            var chatHistory = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));

            try
            {
                var prompt = $"请从以下对话中提取关键事件信息，返回格式：\n关键词1,关键词2,关键词3\n事件描述\n\n{chatHistory}";
                var response = await _llmClient.SendChatAsync(prompt);

                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var keywords = lines.Length > 0 ? lines[0].Split(',', StringSplitOptions.TrimEntries) : Array.Empty<string>();
                var description = lines.Length > 1 ? lines[1] : response[..Math.Min(200, response.Length)];

                var entry = new LongMemoryEntry
                {
                    Id = $"mem_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
                    Keyword1 = keywords.Length > 0 ? keywords[0] : "event",
                    Keyword2 = keywords.Length > 1 ? keywords[1] : "event",
                    Keyword3 = keywords.Length > 2 ? keywords[2] : "event",
                    Description = description,
                    EventTimestamp = DateTime.Now,
                    Importance = 10,
                    CreatedAt = DateTime.Now,
                    LastAccessedAt = DateTime.Now,
                    AccessCount = 0
                };

                await AddEntryAsync(entry);
                _configReader.Logger.Info($"[LongMemory] 从短期记忆总结并存入中期记忆: {entry.Description[..Math.Min(50, entry.Description.Length)]}...");
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[LongMemory] 从短期记忆总结失败: {ex.Message}");
            }
        }
    }
}
