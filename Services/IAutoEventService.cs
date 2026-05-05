namespace catgirlwindow.Services;

/// <summary>
/// 定时任务定义
/// </summary>
public class CronTask
{
    /// <summary>任务唯一标识</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>任务名称（描述）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Cron 表达式（5字段：分 时 日 月 周）</summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>任务类型标识（如 "murmur"、"eye_rest"、"late_night"、"custom"）</summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>任务参数（传递给 Agent 的额外数据）</summary>
    public string? Parameters { get; set; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 自动事件服务接口
/// </summary>
public interface IAutoEventService
{
    /// <summary>启动定时任务调度器</summary>
    void Start();

    /// <summary>停止所有定时任务</summary>
    void Stop();

    // ========== 通用 Cron 任务管理（供 Agent 工具调用） ==========

    /// <summary>注册一个 cron 定时任务</summary>
    void RegisterTask(CronTask task);

    /// <summary>取消一个 cron 定时任务</summary>
    void UnregisterTask(string taskId);

    /// <summary>启用/禁用一个 cron 定时任务</summary>
    void SetTaskEnabled(string taskId, bool enabled);

    /// <summary>获取所有已注册的 cron 定时任务</summary>
    List<CronTask> GetAllTasks();

    /// <summary>获取指定类型的 cron 定时任务</summary>
    List<CronTask> GetTasksByType(string taskType);

    // ========== 内置事件配置 ==========

    /// <summary>设置碎碎念权重（0-100，0=禁用）</summary>
    void SetMurmurWeight(int weight);

    /// <summary>获取碎碎念权重</summary>
    int MurmurWeight { get; }

    /// <summary>设置深夜关怀偏移范围（分钟）</summary>
    /// <param name="minMinutes">最小偏移分钟数</param>
    /// <param name="maxMinutes">最大偏移分钟数</param>
    void SetLateNightOffsetRange(int minMinutes, int maxMinutes);

    /// <summary>获取深夜关怀基准时间</summary>
    TimeSpan LateNightBaseTime { get; }

    /// <summary>获取今日深夜关怀实际触发时间</summary>
    TimeSpan GetTodayLateNightTime();

    // ========== 用户活动检测 ==========

    /// <summary>记录用户活动时间（由外部调用，如鼠标/键盘事件）</summary>
    void RecordUserActivity();

    // ========== 事件 ==========

    /// <summary>定时任务触发事件（参数为触发的 CronTask）</summary>
    event EventHandler<CronTask> OnTaskTriggered;
}
