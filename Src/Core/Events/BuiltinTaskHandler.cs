using System.Text.Json;
using MochiBot.Src.Models;

namespace MochiBot.Src.Core.Events
{
    /// <summary>
    /// 内置任务处理器
    /// 负责碎碎念、用眼提醒、深夜关怀、空闲检测等系统内置事件的检查逻辑
    /// 订阅事件调度器的 SystemAuto 分类，在定时任务触发时执行具体检查
    /// </summary>
    public class BuiltinTaskHandler : IDisposable
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly List<string> _subscriptionIds = new();

        // 用眼提醒
        private DateTime _lastActivityTime = DateTime.Now;
        private bool _eyeRestFired;

        // 深夜关怀
        private TimeSpan? _todayLateNightTime;
        private int _lastLateNightCheckDay;

        // 随机数生成器
        private readonly Random _random = new();

        public BuiltinTaskHandler(IEventDispatcher eventDispatcher)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));

            // 订阅系统自动事件，处理内置任务
            var subId = _eventDispatcher.Subscribe(EventCategory.SystemAuto, OnSystemAutoEvent);
            _subscriptionIds.Add(subId);
        }

        /// <summary>
        /// 处理系统自动事件
        /// 根据事件类型执行对应的内置任务检查
        /// </summary>
        private bool _isHandling;

        private void OnSystemAutoEvent(EventData eventData)
        {
            if (_isHandling) return;
            _isHandling = true;

            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp)) return;

                var type = typeProp.GetString();
                switch (type)
                {
                    case "murmur":
                        HandleMurmur(eventData);
                        break;
                    case "eye_rest":
                        HandleEyeRest(eventData);
                        break;
                    case "late_night":
                        HandleLateNight(eventData);
                        break;
                    case "idle_check":
                        HandleIdleCheck(eventData);
                        break;
                }
            }
            catch { }
            finally
            {
                _isHandling = false;
            }
        }

        /// <summary>
        /// 碎碎念处理
        /// 从任务参数读取权重，随机触发
        /// </summary>
        private void HandleMurmur(EventData eventData)
        {
            // 从事件信息中解析权重
            int weight = 30;
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (doc.RootElement.TryGetProperty("parameters", out var paramsProp))
                {
                    int.TryParse(paramsProp.GetString(), out weight);
                }
            }
            catch { }

            var roll = _random.Next(100);
            if (roll < weight)
            {
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.SystemAuto,
                    Trigger = EventTrigger.System,
                    Info = JsonSerializer.Serialize(new { type = "murmur", name = "碎碎念" })
                });
            }
        }

        /// <summary>
        /// 用眼提醒处理
        /// 从任务参数读取阈值，检查用户活动时间
        /// </summary>
        private void HandleEyeRest(EventData eventData)
        {
            if (_eyeRestFired) return;

            // 从事件信息中解析阈值
            int thresholdMinutes = 120;
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (doc.RootElement.TryGetProperty("parameters", out var paramsProp))
                {
                    int.TryParse(paramsProp.GetString(), out thresholdMinutes);
                }
            }
            catch { }

            var elapsed = DateTime.Now - _lastActivityTime;
            if (elapsed.TotalMinutes >= thresholdMinutes)
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

        /// <summary>
        /// 深夜关怀处理
        /// 从任务参数读取偏移范围，计算今日触发时间
        /// </summary>
        private void HandleLateNight(EventData eventData)
        {
            var now = DateTime.Now;
            var todayLateNight = GetTodayLateNightTime(eventData);
            var currentTime = now.TimeOfDay;
            var diff = (currentTime - todayLateNight).TotalSeconds;

            if (diff >= 0 && diff < 2)
            {
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.SystemAuto,
                    Trigger = EventTrigger.System,
                    Info = JsonSerializer.Serialize(new { type = "late_night", name = "深夜关怀" })
                });
            }
        }

        /// <summary>
        /// 空闲检测处理
        /// 从任务参数读取阈值，检查用户空闲时间
        /// </summary>
        private void HandleIdleCheck(EventData eventData)
        {
            // 从事件信息中解析阈值
            int thresholdMinutes = 5;
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (doc.RootElement.TryGetProperty("parameters", out var paramsProp))
                {
                    int.TryParse(paramsProp.GetString(), out thresholdMinutes);
                }
            }
            catch { }

            var idleMinutes = (DateTime.Now - _lastActivityTime).TotalMinutes;
            if (idleMinutes >= thresholdMinutes)
            {
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.SystemAuto,
                    Trigger = EventTrigger.System,
                    Info = JsonSerializer.Serialize(new { type = "idle", minutes = (int)idleMinutes, name = "空闲检测" })
                });
            }
        }

        /// <summary>
        /// 获取今日深夜关怀触发时间
        /// </summary>
        private TimeSpan GetTodayLateNightTime(EventData eventData)
        {
            var today = DateTime.Now.DayOfYear;
            if (_todayLateNightTime == null || _lastLateNightCheckDay != today)
            {
                _todayLateNightTime = CalculateTodayLateNightTime(eventData);
                _lastLateNightCheckDay = today;
            }
            return _todayLateNightTime.Value;
        }

        /// <summary>
        /// 计算今日深夜关怀触发时间
        /// </summary>
        private TimeSpan CalculateTodayLateNightTime(EventData eventData)
        {
            // 从事件信息中解析偏移范围
            int offsetMin = -30, offsetMax = 30;
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (doc.RootElement.TryGetProperty("parameters", out var paramsProp))
                {
                    var paramStr = paramsProp.GetString();
                    if (paramStr != null)
                    {
                        var parts = paramStr.Split(',');
                        if (parts.Length == 2)
                        {
                            int.TryParse(parts[0], out offsetMin);
                            int.TryParse(parts[1], out offsetMax);
                        }
                    }
                }
            }
            catch { }

            // 默认基准时间 23:00
            const int baseHour = 23;
            const int baseMinute = 0;

            var baseMinutes = baseHour * 60 + baseMinute;
            var offset = _random.Next(offsetMin, offsetMax + 1);
            var totalMinutes = baseMinutes + offset;
            totalMinutes = (totalMinutes % 1440 + 1440) % 1440;
            return TimeSpan.FromMinutes(totalMinutes);
        }

        /// <summary>
        /// 记录用户活动时间
        /// </summary>
        public void RecordUserActivity()
        {
            _lastActivityTime = DateTime.Now;
            _eyeRestFired = false;
        }

        public void Dispose()
        {
            foreach (var id in _subscriptionIds)
            {
                _eventDispatcher.Unsubscribe(id);
            }
            _subscriptionIds.Clear();
        }
    }
}
