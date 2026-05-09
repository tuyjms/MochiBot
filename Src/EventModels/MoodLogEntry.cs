namespace MochiBot.Src.EventModels
{
    /// <summary>
    /// 情绪日志条目
    /// </summary>
    public class MoodLogEntry
    {
        public DateTime Timestamp { get; set; }
        public AgentMood Mood { get; set; }
        public string Trigger { get; set; } = string.Empty;
    }
}
