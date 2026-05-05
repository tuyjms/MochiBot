using catgirlwindow.Models;

namespace catgirlwindow.Services;

/// <summary>
/// 数据库服务接口
/// </summary>
public interface IDatabaseService
{
    // ========== 用户配置 ==========

    /// <summary>加载用户配置</summary>
    Task<UserConfig> LoadConfigAsync();

    /// <summary>保存用户配置</summary>
    Task SaveConfigAsync(UserConfig config);


    // ========== 聊天记录 ==========

    /// <summary>保存聊天记录到历史</summary>
    /// <param name="messages">要保存的消息列表</param>
    Task SaveChatHistoryAsync(List<ChatMessage> messages);

    /// <summary>加载历史聊天记录</summary>
    /// <param name="limit">加载条数，默认50条</param>
    Task<List<ChatMessage>> LoadChatHistoryAsync(int limit = 50);


    // ========== 情绪日志 ==========

    /// <summary>记录情绪变化</summary>
    /// <param name="mood">变化后的情绪</param>
    /// <param name="trigger">触发原因</param>
    Task LogMoodChangeAsync(AgentMood mood, string trigger);

    /// <summary>查询指定时间范围内的情绪日志</summary>
    Task<List<MoodLogEntry>> GetMoodLogAsync(DateTime start, DateTime end);
}

/// <summary>
/// 情绪日志条目
/// </summary>
public class MoodLogEntry
{
    public DateTime Timestamp { get; set; }
    public AgentMood Mood { get; set; }
    public string Trigger { get; set; } = string.Empty;
}
