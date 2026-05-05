namespace catgirlwindow.Services;

/// <summary>
/// 长期记忆条目
/// </summary>
public class LongTermMemoryEntry
{
    /// <summary>唯一标识</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>关键词1（主词，通常是主语）</summary>
    public string Keyword1 { get; set; } = string.Empty;

    /// <summary>关键词2（通常是谓语/动作）</summary>
    public string Keyword2 { get; set; } = string.Empty;

    /// <summary>关键词3（通常是宾语/对象）</summary>
    public string Keyword3 { get; set; } = string.Empty;

    /// <summary>事件描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>事件发生的时间戳</summary>
    public DateTime EventTimestamp { get; set; }

    /// <summary>重要度 0-100</summary>
    public int Importance { get; set; }

    /// <summary>记录创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>最后访问时间（用于LRU淘汰）</summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.Now;

    /// <summary>访问次数（用于热数据识别）</summary>
    public int AccessCount { get; set; } = 0;

    /// <summary>来源：MidTermPromotion / ThresholdTrigger</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// 长期记忆接口
/// </summary>
public interface ILongTermMemory
{
    /// <summary>录入一条长期记忆</summary>
    Task AddEntryAsync(LongTermMemoryEntry entry);

    /// <summary>批量录入长期记忆</summary>
    Task AddEntriesAsync(IEnumerable<LongTermMemoryEntry> entries);

    /// <summary>根据关键词搜索长期记忆</summary>
    /// <param name="keywords">搜索关键词列表</param>
    /// <param name="topN">返回前N条结果</param>
    Task<List<LongTermMemoryEntry>> SearchByKeywordsAsync(List<string> keywords, int topN = 5);

    /// <summary>获取重要度高于指定阈值的记录</summary>
    Task<List<LongTermMemoryEntry>> GetEntriesByImportanceAsync(int minImportance);

    /// <summary>获取指定时间范围内的记录</summary>
    Task<List<LongTermMemoryEntry>> GetEntriesByTimeRangeAsync(DateTime start, DateTime end);

    /// <summary>获取最近N条记录</summary>
    Task<List<LongTermMemoryEntry>> GetRecentEntriesAsync(int count = 20);

    /// <summary>根据ID获取记录</summary>
    Task<LongTermMemoryEntry?> GetEntryByIdAsync(string id);

    /// <summary>更新记录的访问信息</summary>
    Task UpdateAccessInfoAsync(string entryId);

    /// <summary>删除指定记录</summary>
    Task DeleteEntryAsync(string entryId);

    /// <summary>获取总记录数</summary>
    Task<int> GetCountAsync();

    /// <summary>清空所有长期记忆</summary>
    Task ClearAsync();
}
