using System.Text.Json;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using static MochiBot.Src.EventModels.MoodEventTypes;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 心情管理器
    /// 负责情绪状态切换、情绪事件发布、情绪日志持久化
    /// 以及从用户消息中自动检测情绪触发词
    /// </summary>
    public class MoodManager
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly MoodLogRepository? _moodLogRepository;

        private AgentMood _currentMood = AgentMood.Neutral;

        /// <summary>获取当前情绪</summary>
        public AgentMood CurrentMood => _currentMood;

        public MoodManager(IEventDispatcher eventDispatcher, MoodLogRepository? moodLogRepository = null)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _moodLogRepository = moodLogRepository;
        }

        /// <summary>
        /// 根据事件类型切换心情，并通过事件调度器发布 MoodChange 事件
        /// </summary>
        /// <param name="eventType">事件类型字符串（来自 MoodEventTypes 常量）</param>
        public void ChangeMoodByEvent(string eventType)
        {
            var newMood = eventType switch
            {
                LateNight or Sleepy => AgentMood.Sleepy,
                LongWork => AgentMood.Neutral,
                Idle => AgentMood.Sad,
                Active => AgentMood.Neutral,
                Pet => AgentMood.Touched,
                Compliment => AgentMood.Happy,
                Angry => AgentMood.Angry,
                _ => _currentMood
            };

            if (_currentMood == newMood) return;
            _currentMood = newMood;

            // 通过事件调度器发布情绪变化事件
            _eventDispatcher.Publish(new EventData
            {
                Category = EventCategory.MoodChange,
                Trigger = EventTrigger.System,
                Info = JsonSerializer.Serialize(new
                {
                    mood = newMood.ToString(),
                    source = eventType
                })
            });

            // 记录到数据库
            if (_moodLogRepository != null)
            {
                _ = _moodLogRepository.LogMoodChangeAsync(newMood, eventType);
            }
        }

        /// <summary>
        /// 根据用户消息内容和时间自动检测并触发情绪事件
        /// </summary>
        /// <param name="userMessage">用户消息文本</param>
        /// <returns>触发的情绪事件类型（用于调用方更新 _lastEvent），未触发时返回 null</returns>
        public string? DetectMoodEvent(string userMessage)
        {
            var hour = DateTime.Now.Hour;
            if (hour >= 23 || hour < 6)
            {
                ChangeMoodByEvent(LateNight);
                return LateNight;
            }

            var msg = userMessage.ToLowerInvariant();

            if (msg.Contains("摸摸") || msg.Contains("摸头") || msg.Contains("拍头") || msg.Contains("抱抱"))
            {
                ChangeMoodByEvent(Pet);
                return Pet;
            }

            if (msg.Contains("夸") || msg.Contains("好看") || msg.Contains("可爱") || msg.Contains("漂亮") ||
                msg.Contains("喜欢你") || msg.Contains("真棒") || msg.Contains("厉害"))
            {
                ChangeMoodByEvent(Compliment);
                return Compliment;
            }

            ChangeMoodByEvent(Active);
            return Active;
        }
    }
}
