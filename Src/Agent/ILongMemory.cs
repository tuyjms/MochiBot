using MochiBot.Src.EventModels;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 长期记忆条目
    /// </summary>
    public class LongMemoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Keyword1 { get; set; } = string.Empty;
        public string Keyword2 { get; set; } = string.Empty;
        public string Keyword3 { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime EventTimestamp { get; set; } = DateTime.Now;
        public int Importance { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastAccessedAt { get; set; } = DateTime.Now;
        public int AccessCount { get; set; }
    }

    /// <summary>
    /// 长期记忆接口
    /// 负责长期记忆的录入、检索、晋升和淘汰
    /// </summary>
    public interface ILongMemory
    {
        /// <summary>添加一条长期记忆条目</summary>
        Task AddEntryAsync(LongMemoryEntry entry);

        /// <summary>根据关键词搜索长期记忆</summary>
        Task<List<LongMemoryEntry>> SearchByKeywordsAsync(string keyword);

        /// <summary>根据重要度筛选长期记忆</summary>
        Task<List<LongMemoryEntry>> GetByImportanceAsync(int minImportance);

        /// <summary>获取指定时间范围内的长期记忆</summary>
        Task<List<LongMemoryEntry>> GetByTimeRangeAsync(DateTime start, DateTime end);

        /// <summary>更新访问时间和访问次数</summary>
        Task UpdateAccessAsync(string entryId);

        /// <summary>删除一条长期记忆</summary>
        Task DeleteEntryAsync(string entryId);

        /// <summary>清空所有长期记忆</summary>
        Task ClearAllAsync();

        /// <summary>获取长期记忆总数</summary>
        Task<int> GetCountAsync();

        /// <summary>晋升机制：将访问次数超过阈值的条目提升重要度</summary>
        Task PromoteEntriesAsync(int accessThreshold, int importanceIncrement);

        /// <summary>淘汰机制：删除重要度低于阈值且长期未访问的条目</summary>
        Task EvictEntriesAsync(int minImportance, int maxInactiveDays);

        /// <summary>传入短期记忆，LLM总结事件并存入中期记忆</summary>
        Task SummarizeShortTermAsync(IShortTermMemory shortTermMemory);
    }
}
