using catgirlwindow.Src.Models;

namespace catgirlwindow.Src.Core.Events
{
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
    /// 事件调度器接口
    /// 统一管理所有事件的发布和订阅，以及定时任务调度
    /// </summary>
    public interface IEventDispatcher
    {
        // ========== 事件发布/订阅 ==========

        /// <summary>发布事件</summary>
        void Publish(EventData eventData);

        /// <summary>订阅指定分类的事件</summary>
        string Subscribe(EventCategory category, Action<EventData> handler);

        /// <summary>订阅所有事件</summary>
        string SubscribeAll(Action<EventData> handler);

        /// <summary>取消订阅</summary>
        void Unsubscribe(string subscriptionId);

        /// <summary>获取指定分类的订阅者数量</summary>
        int GetSubscriberCount(EventCategory category);

        // ========== 定时任务管理（原 AutoEventService） ==========

        /// <summary>启动定时任务调度器</summary>
        void StartScheduler();

        /// <summary>停止定时任务调度器</summary>
        void StopScheduler();

        /// <summary>注册一个 cron 定时任务</summary>
        void RegisterTask(CronTask task);

        /// <summary>取消一个 cron 定时任务</summary>
        void UnregisterTask(string taskId);

        /// <summary>启用/禁用一个 cron 定时任务</summary>
        void SetTaskEnabled(string taskId, bool enabled);

        /// <summary>获取所有已注册的 cron 定时任务</summary>
        List<CronTask> GetAllTasks();

        // ========== 用户活动检测 ==========

        /// <summary>获得用户活动时间（由外部调用，如鼠标/键盘事件）</summary>
        TimeSpan RecordUserActivity();
    }
}
