using catgirlwindow.Models;

namespace catgirlwindow.Services;

/// <summary>
/// 简单的情绪跟踪器实现（临时版本，后续由其他成员完善）
/// </summary>
public class AgentMoodTracker : IAgentMoodTracker
{
    public AgentMood CurrentMood { get; private set; } = AgentMood.Neutral;

    public event EventHandler<AgentMood>? MoodChanged;

    public void SetMood(AgentMood mood)
    {
        if (CurrentMood == mood) return;
        CurrentMood = mood;
        MoodChanged?.Invoke(this, mood);
    }

    public void UpdateMoodByEvent(string eventType)
    {
        var newMood = eventType switch
        {
            "LateNight" or "Sleepy" => AgentMood.Sleepy,
            "LongWork" => AgentMood.Neutral,
            "Idle" => AgentMood.Sad,
            "Active" => AgentMood.Neutral,
            "Pet" => AgentMood.Touched,
            "Compliment" => AgentMood.Happy,
            "Angry" => AgentMood.Angry,
            _ => CurrentMood
        };

        SetMood(newMood);
    }

    public string GetMoodImagePath()
    {
        return CurrentMood switch
        {
            AgentMood.Happy => "Resources/Images/happy.png",
            AgentMood.Sad => "Resources/Images/sad.png",
            AgentMood.Sleepy => "Resources/Images/sleepy.png",
            AgentMood.Touched => "Resources/Images/touched.png",
            AgentMood.Angry => "Resources/Images/angry.png",
            _ => "Resources/Images/neutral.png"
        };
    }
}
