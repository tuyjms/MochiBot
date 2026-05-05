using catgirlwindow.Models;

namespace catgirlwindow.Services;

/// <summary>
/// 心理状态记录器接口
/// </summary>
public interface IAgentMoodTracker
{
    /// <summary>获取当前情绪</summary>
    AgentMood CurrentMood { get; }

    /// <summary>手动设置情绪（外部触发，如摸摸她）</summary>
    /// <param name="mood">目标情绪</param>
    void SetMood(AgentMood mood);

    /// <summary>根据系统事件自动切换情绪</summary>
    /// <param name="eventType">事件类型：LateNight, LongWork, Idle, Active, Pet, Compliment</param>
    void UpdateMoodByEvent(string eventType);

    /// <summary>获取当前情绪对应的表情图片路径</summary>
    string GetMoodImagePath();

    /// <summary>情绪变化时触发的事件（UI订阅以更新头像）</summary>
    event EventHandler<AgentMood> MoodChanged;
}
