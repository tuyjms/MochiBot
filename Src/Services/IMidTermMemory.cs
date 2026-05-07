using MochiBot.Src.Models;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 中期记忆条目
    /// </summary>
    public class MidTermMemoryEntry
    {
        /// <summary>唯一标识</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>事件发生的时间戳</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>事件描述（对话摘要或关键信息）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>重要度 0-100（越高越重要）</summary>
        public int Importance { get; set; }

        /// <summary>来源：LLM / Overflow / KeywordScan</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>关联的短期记忆消息ID列表（可选）</summary>
        public List<string>? RelatedMessageIds { get; set; }

        /// <summary>是否已提升到长期记忆</summary>
        public bool PromotedToLongTerm { get; set; } = false;
    }

    /// <summary>
    /// 中期记忆接口
    /// </summary>
    public interface IMidTermMemory
    {
        /// <summary>录入一条中期记忆</summary>
        Task AddEntryAsync(MidTermMemoryEntry entry);

        /// <summary>批量录入中期记忆</summary>
        Task AddEntriesAsync(IEnumerable<MidTermMemoryEntry> entries);

        /// <summary>获取所有中期记忆（按重要度降序）</summary>
        Task<List<MidTermMemoryEntry>> GetAllEntriesAsync();

        /// <summary>获取重要度高于指定阈值的记录</summary>
        Task<List<MidTermMemoryEntry>> GetEntriesByImportanceAsync(int minImportance);

        /// <summary>获取指定时间范围内的记录</summary>
        Task<List<MidTermMemoryEntry>> GetEntriesByTimeRangeAsync(DateTime start, DateTime end);

        /// <summary>获取最近N条记录</summary>
        Task<List<MidTermMemoryEntry>> GetRecentEntriesAsync(int count = 20);

        /// <summary>标记记录已提升到长期记忆</summary>
        Task MarkAsPromotedAsync(string entryId);

        /// <summary>删除指定记录</summary>
        Task DeleteEntryAsync(string entryId);

        /// <summary>获取总记录数</summary>
        Task<int> GetCountAsync();

        /// <summary>清空所有中期记忆</summary>
        Task ClearAsync();
    }
}
