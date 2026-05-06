using System.Text.Json;
using System.Timers;
using catgirlwindow.Src.Core.Events;
using catgirlwindow.Src.Models;
using Timer = System.Timers.Timer;

namespace catgirlwindow.Src.Services
{
    /// <summary>
    /// 自动事件服务 - 通用定时任务调度器 + 三个内置特殊事件
    /// 通过事件调度器发布事件，不再直接触发 OnTaskTriggered
    /// </summary>
    public class AutoEventService : IAutoEventService, IDisposable
    {
        private readonly IEventDispatcher _eventDispatcher;
        private Timer? _mainTimer;
        private bool _isRunning;
        private int _tickCount;

        // 已注册的 cron 任务列表
        private readonly List<CronTask> _tasks = new();

        // 碎碎念
        private int _murmurWeight = 30;

        // 用眼提醒
        private DateTime _startTime;
        private DateTime _lastActivityTime;
        private const int EyeRestThresholdMinutes = 120;
        private bool _eyeRestFired;

        // 深夜关怀
        private const int LateNightBaseHour = 23;
        private const int LateNightBaseMinute = 0;
        private int _lateNightOffsetMin = -30;
        private int _lateNightOffsetMax = 30;
        private TimeSpan? _todayLateNightTime;
        private int _lastLateNightCheckDay;

        // 随机数生成器
        private readonly Random _random = new();

        // ========== 接口实现 ==========

        public int MurmurWeight => _murmurWeight;
        public TimeSpan LateNightBaseTime => new(LateNightBaseHour, LateNightBaseMinute, 0);

        public event EventHandler<CronTask>? OnTaskTriggered;

        public AutoEventService(IEventDispatcher eventDispatcher)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            _tickCount = 0;
            _startTime = DateTime.Now;
            _lastActivityTime = DateTime.Now;
            _eyeRestFired = false;
            _lastLateNightCheckDay = DateTime.Now.DayOfYear;
            _todayLateNightTime = CalculateTodayLateNightTime();

            _mainTimer = new Timer(1000); // 每秒 tick 一次
            _mainTimer.Elapsed += OnMainTimerElapsed;
            _mainTimer.AutoReset = true;
            _mainTimer.Start();
        }

        public void Stop()
        {
            _isRunning = false;

            _mainTimer?.Stop();
            _mainTimer?.Dispose();
            _mainTimer = null;
        }

        // ========== Cron 任务管理 ==========

        public void RegisterTask(CronTask task)
        {
            lock (_tasks)
            {
                // 如果已存在相同 ID 的任务，先移除
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

        public List<CronTask> GetTasksByType(string taskType)
        {
            lock (_tasks)
            {
                return _tasks.Where(t => t.TaskType == taskType).ToList();
            }
        }

        // ========== 内置事件配置 ==========

        public void SetMurmurWeight(int weight)
        {
            _murmurWeight = Math.Clamp(weight, 0, 100);
        }

        public void SetLateNightOffsetRange(int minMinutes, int maxMinutes)
        {
            _lateNightOffsetMin = minMinutes;
            _lateNightOffsetMax = maxMinutes;
            // 清除缓存，下次获取时重新计算
            _todayLateNightTime = null;
        }

        public TimeSpan GetTodayLateNightTime()
        {
            var today = DateTime.Now.DayOfYear;
            if (_todayLateNightTime == null || _lastLateNightCheckDay != today)
            {
                _todayLateNightTime = CalculateTodayLateNightTime();
                _lastLateNightCheckDay = today;
            }
            return _todayLateNightTime.Value;
        }

        // ========== 用户活动检测 ==========

        public void RecordUserActivity()
        {
            _lastActivityTime = DateTime.Now;
            _eyeRestFired = false;
        }

        // ========== 主循环 ==========

        private void OnMainTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_isRunning) return;

            _tickCount++;

            // 1. 检查所有 cron 任务
            CheckCronTasks();

            // 2. 每 10 tick 检查碎碎念
            if (_tickCount % 10 == 0)
                CheckMurmur();

            // 3. 检查用眼提醒
            CheckEyeRest();

            // 4. 检查深夜关怀
            CheckLateNight();
        }

        // ========== Cron 任务检查 ==========

        private void CheckCronTasks()
        {
            var now = DateTime.Now;

            lock (_tasks)
            {
                foreach (var task in _tasks)
                {
                    if (!task.Enabled) continue;

                    if (MatchesCron(task.CronExpression, now))
                    {
                        OnTaskTriggered?.Invoke(this, task);
                    }
                }
            }
        }

        // ========== 碎碎念检查 ==========

        private void CheckMurmur()
        {
            if (_murmurWeight <= 0) return;

            var roll = _random.Next(100);
            if (roll < _murmurWeight)
            {
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.SystemAuto,
                    Trigger = EventTrigger.System,
                    Info = JsonSerializer.Serialize(new { type = "murmur", name = "碎碎念" })
                });
            }
        }

        // ========== 用眼提醒检查 ==========

        private void CheckEyeRest()
        {
            if (_eyeRestFired) return;

            var elapsed = DateTime.Now - _lastActivityTime;
            if (elapsed.TotalMinutes >= EyeRestThresholdMinutes)
            {
                _eyeRestFired = true;
                var hours = (int)elapsed.TotalHours;
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.SystemAuto,
                    Trigger = EventTrigger.System,
                    Info = JsonSerializer.Serialize(new { type = "eye_rest", hours, name = "用眼提醒" })
                });
            }
        }

        // ========== 深夜关怀检查 ==========

        private void CheckLateNight()
        {
            var now = DateTime.Now;
            var todayLateNight = GetTodayLateNightTime();

            // 检查是否到达今日触发时间（允许1分钟误差窗口）
            var currentTime = now.TimeOfDay;
            var diff = (currentTime - todayLateNight).TotalSeconds;

            if (diff >= 0 && diff < 2) // 1秒误差窗口
            {
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.SystemAuto,
                    Trigger = EventTrigger.System,
                    Info = JsonSerializer.Serialize(new { type = "late_night", name = "深夜关怀" })
                });
            }
        }

        // ========== 辅助方法 ==========

        private TimeSpan CalculateTodayLateNightTime()
        {
            var baseMinutes = LateNightBaseHour * 60 + LateNightBaseMinute;
            var offset = _random.Next(_lateNightOffsetMin, _lateNightOffsetMax + 1);
            var totalMinutes = baseMinutes + offset;

            // 确保在 0~1439 范围内
            totalMinutes = (totalMinutes % 1440 + 1440) % 1440;

            return TimeSpan.FromMinutes(totalMinutes);
        }

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

            // 步长：*/N
            if (field.StartsWith("*/"))
            {
                if (int.TryParse(field[2..], out var step) && step > 0)
                    return value % step == 0;
                return false;
            }

            // 列表：1,15,30
            if (field.Contains(','))
            {
                var parts = field.Split(',');
                return parts.Any(p => MatchField(p.Trim(), value, min, max));
            }

            // 范围：9-18
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

            // 精确值
            if (int.TryParse(field, out var exact))
            {
                // 星期天特殊处理：0 和 7 都表示周日
                if (max == 7)
                {
                    if (exact == 0 || exact == 7)
                        return value == 0 || value == 7;
                }
                return value == exact;
            }

            return false;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
