using System.Collections.Concurrent;
using System.Text.Json;
using System.Timers;
using catgirlwindow.Src.Models;
using Timer = System.Timers.Timer;

namespace catgirlwindow.Src.Core.Events
{
    /// <summary>
    /// 事件调度器实现
    /// 统一管理所有事件的发布和订阅，以及定时任务调度
    /// 支持同步和异步两种订阅模式
    /// </summary>
    public class EventDispatcher : IEventDispatcher, IDisposable
    {
        // ========== 事件订阅 ==========
        private readonly ConcurrentDictionary<EventCategory, List<Subscription>> _categorySubscriptions = new();
        private readonly List<Subscription> _allSubscriptions = new();
        private readonly Lock _lock = new();

        // ========== 定时任务调度 ==========
        private Timer? _mainTimer;
        private bool _isRunning;
        private int _tickCount;
        private readonly List<CronTask> _tasks = new();

        // ========== 事件发布/订阅 ==========

        public void Publish(EventData eventData)
        {
            // 通知指定分类的订阅者
            if (_categorySubscriptions.TryGetValue(eventData.Category, out var categorySubs))
            {
                List<Subscription> snapshot;
                lock (_lock)
                {
                    snapshot = new List<Subscription>(categorySubs);
                }
                foreach (var sub in snapshot)
                {
                    try
                    {
                        if (sub.SyncHandler != null)
                            sub.SyncHandler(eventData);
                        else if (sub.AsyncHandler != null)
                            _ = sub.AsyncHandler(eventData); // fire-and-forget
                    }
                    catch { /* 防止单个订阅者异常影响其他订阅者 */ }
                }
            }

            // 通知所有事件的订阅者
            List<Subscription> allSnapshot;
            lock (_lock)
            {
                allSnapshot = new List<Subscription>(_allSubscriptions);
            }
            foreach (var sub in allSnapshot)
            {
                try
                {
                    if (sub.SyncHandler != null)
                        sub.SyncHandler(eventData);
                    else if (sub.AsyncHandler != null)
                        _ = sub.AsyncHandler(eventData); // fire-and-forget
                }
                catch { /* 防止单个订阅者异常影响其他订阅者 */ }
            }
        }

        public async Task PublishAsync(EventData eventData)
        {
            var tasks = new List<Task>();

            // 通知指定分类的订阅者
            if (_categorySubscriptions.TryGetValue(eventData.Category, out var categorySubs))
            {
                List<Subscription> snapshot;
                lock (_lock)
                {
                    snapshot = new List<Subscription>(categorySubs);
                }
                foreach (var sub in snapshot)
                {
                    try
                    {
                        if (sub.AsyncHandler != null)
                            tasks.Add(sub.AsyncHandler(eventData));
                        else if (sub.SyncHandler != null)
                            sub.SyncHandler(eventData);
                    }
                    catch { /* 防止单个订阅者异常影响其他订阅者 */ }
                }
            }

            // 通知所有事件的订阅者
            List<Subscription> allSnapshot;
            lock (_lock)
            {
                allSnapshot = new List<Subscription>(_allSubscriptions);
            }
            foreach (var sub in allSnapshot)
            {
                try
                {
                    if (sub.AsyncHandler != null)
                        tasks.Add(sub.AsyncHandler(eventData));
                    else if (sub.SyncHandler != null)
                        sub.SyncHandler(eventData);
                }
                catch { /* 防止单个订阅者异常影响其他订阅者 */ }
            }

            // 等待所有异步订阅者完成（捕获异常防止影响调用者）
            if (tasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch
                {
                    // 单个订阅者的异常已在添加 task 时被 catch，但 Task.WhenAll 会重新抛出
                    // 这里再次捕获以确保不会影响其他订阅者或调用者
                }
            }
        }

        public string Subscribe(EventCategory category, Action<EventData> handler)
        {
            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                SyncHandler = handler
            };

            lock (_lock)
            {
                var subs = _categorySubscriptions.GetOrAdd(category, _ => new List<Subscription>());
                subs.Add(subscription);
            }

            return subscription.Id;
        }

        public string Subscribe(EventCategory category, Func<EventData, Task> handler)
        {
            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                AsyncHandler = handler
            };

            lock (_lock)
            {
                var subs = _categorySubscriptions.GetOrAdd(category, _ => new List<Subscription>());
                subs.Add(subscription);
            }

            return subscription.Id;
        }

        public string SubscribeAll(Action<EventData> handler)
        {
            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                SyncHandler = handler
            };

            lock (_lock)
            {
                _allSubscriptions.Add(subscription);
            }

            return subscription.Id;
        }

        public string SubscribeAll(Func<EventData, Task> handler)
        {
            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                AsyncHandler = handler
            };

            lock (_lock)
            {
                _allSubscriptions.Add(subscription);
            }

            return subscription.Id;
        }

        public void Unsubscribe(string subscriptionId)
        {
            lock (_lock)
            {
                foreach (var kvp in _categorySubscriptions)
                {
                    kvp.Value.RemoveAll(s => s.Id == subscriptionId);
                }
                _allSubscriptions.RemoveAll(s => s.Id == subscriptionId);
            }
        }

        public int GetSubscriberCount(EventCategory category)
        {
            lock (_lock)
            {
                if (_categorySubscriptions.TryGetValue(category, out var subs))
                    return subs.Count;
                return 0;
            }
        }

        // ========== 定时任务管理 ==========

        public void StartScheduler()
        {
            if (_isRunning) return;
            _isRunning = true;

            _tickCount = 0;

            _mainTimer = new Timer(1000);
            _mainTimer.Elapsed += OnMainTimerElapsed;
            _mainTimer.AutoReset = true;
            _mainTimer.Start();
        }

        public void StopScheduler()
        {
            _isRunning = false;
            _mainTimer?.Stop();
            _mainTimer?.Dispose();
            _mainTimer = null;
        }

        public void RegisterTask(CronTask task)
        {
            lock (_tasks)
            {
                var existing = _tasks.FirstOrDefault(t => t.Id == task.Id);
                if (existing != null)
                    _tasks.Remove(existing);
                _tasks.Add(task);
            }
        }

        public void UnregisterTask(string taskId)
        {
            lock (_tasks)
            {
                _tasks.RemoveAll(t => t.Id == taskId);
            }
        }

        public void SetTaskEnabled(string taskId, bool enabled)
        {
            lock (_tasks)
            {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task != null)
                    task.Enabled = enabled;
            }
        }

        public List<CronTask> GetAllTasks()
        {
            lock (_tasks)
            {
                return new List<CronTask>(_tasks);
            }
        }

        public TimeSpan RecordUserActivity()
        {
            return DateTime.Now.TimeOfDay;
        }

        // ========== 主循环 ==========

        private void OnMainTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_isRunning) return;
            _tickCount++;

            // 检查所有 cron 任务，匹配的发布为 SystemAuto 事件
            CheckCronTasks();
        }

        // ========== Cron 任务检查 ==========

        // 记录每个任务上次触发的时间（分钟级），防止同一分钟内重复触发
        private readonly Dictionary<string, int> _lastTaskTriggerMinute = new();

        private void CheckCronTasks()
        {
            var now = DateTime.Now;
            var currentMinute = now.Hour * 60 + now.Minute;

            lock (_tasks)
            {
                foreach (var task in _tasks)
                {
                    if (!task.Enabled) continue;

                    // 检查是否在同一分钟内已经触发过
                    if (_lastTaskTriggerMinute.TryGetValue(task.Id, out var lastMinute) && lastMinute == currentMinute)
                        continue;

                    if (MatchesCron(task.CronExpression, now))
                    {
                        _lastTaskTriggerMinute[task.Id] = currentMinute;

                        Publish(new EventData
                        {
                            Category = EventCategory.SystemAuto,
                            Trigger = EventTrigger.System,
                            Info = JsonSerializer.Serialize(new
                            {
                                type = task.TaskType,
                                name = task.Name,
                                taskId = task.Id,
                                parameters = task.Parameters
                            })
                        });
                    }
                }
            }
        }

        // ========== Cron 表达式匹配 ==========

        /// <summary>
        /// 判断给定时间是否匹配 Cron 表达式
        /// </summary>
        public static bool MatchesCron(string expression, DateTime time)
        {
            var fields = expression.Trim().Split(' ');
            if (fields.Length != 5)
                return false;

            try
            {
                return MatchField(fields[0], time.Minute, 0, 59)
                    && MatchField(fields[1], time.Hour, 0, 23)
                    && MatchField(fields[2], time.Day, 1, 31)
                    && MatchField(fields[3], time.Month, 1, 12)
                    && MatchField(fields[4], (int)time.DayOfWeek, 0, 7);
            }
            catch
            {
                return false;
            }
        }

        private static bool MatchField(string field, int value, int min, int max)
        {
            if (field == "*")
                return true;

            if (field.StartsWith("*/"))
            {
                if (int.TryParse(field[2..], out var step) && step > 0)
                    return value % step == 0;
                return false;
            }

            if (field.Contains(','))
            {
                var parts = field.Split(',');
                return parts.Any(p => MatchField(p.Trim(), value, min, max));
            }

            if (field.Contains('-'))
            {
                var parts = field.Split('-');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out var rangeStart)
                    && int.TryParse(parts[1], out var rangeEnd))
                {
                    return value >= rangeStart && value <= rangeEnd;
                }
                return false;
            }

            if (int.TryParse(field, out var exact))
            {
                if (max == 7)
                {
                    if (exact == 0 || exact == 7)
                        return value == 0 || value == 7;
                }
                return value == exact;
            }

            return false;
        }

        // ========== 订阅记录 ==========

        private class Subscription
        {
            public string Id { get; set; } = string.Empty;
            public Action<EventData>? SyncHandler { get; set; }
            public Func<EventData, Task>? AsyncHandler { get; set; }
        }

        public void Dispose()
        {
            StopScheduler();
            GC.SuppressFinalize(this);
        }
    }
}
