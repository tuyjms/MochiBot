using System.Text.Json;
using MochiBot.Src.EventModels;
using static MochiBot.Src.Core.Constants;

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

        // 深夜关怀（随机偏移计算）
        private TimeSpan? _todayLateNightTime;
        private int _lastLateNightCheckDay;
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
                    case BuiltinTasks.Murmur:
                        HandleMurmur(eventData);
                        break;
                    case BuiltinTasks.EyeRest:
                        HandleEyeRest(eventData);
                        break;
                    case BuiltinTasks.LateNight:
                        HandleLateNight(eventData);
                        break;
                    case BuiltinTasks.IdleCheck:
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
        /// 权重判断已移至 Agent.TryHandleMurmur，此处不再二次发布事件
        /// </summary>
        private void HandleMurmur(EventData eventData)
        {
            // 权重判断和 LLM/内置文本选择已由 Agent.TryHandleMurmur 统一处理
            // 此处无需额外操作，避免重复触发 LLM 请求
        }

        /// <summary>
        /// 用眼提醒处理
        /// 条件检查已移至 Agent.ProcessEventInternalAsync，此处不再二次发布事件
        /// </summary>
        private void HandleEyeRest(EventData eventData)
        {
            // 用眼提醒的条件判断（阈值检查）已由 Agent 直接处理
            // 此处无需额外操作，避免重复触发 LLM 请求
        }

        /// <summary>
        /// 深夜关怀处理
        /// 条件检查已移至 Agent.ProcessEventInternalAsync，此处不再二次发布事件
        /// </summary>
        private void HandleLateNight(EventData eventData)
        {
            // 深夜关怀的时间判断已由 Agent 直接处理
            // 此处无需额外操作，避免重复触发 LLM 请求
        }

        /// <summary>
        /// 空闲检测处理
        /// 条件检查已移至 Agent.ProcessEventInternalAsync，此处不再二次发布事件
        /// </summary>
        private void HandleIdleCheck(EventData eventData)
        {
            // 空闲检测的条件判断（阈值检查）已由 Agent 直接处理
            // 此处无需额外操作，避免重复触发 LLM 请求
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
