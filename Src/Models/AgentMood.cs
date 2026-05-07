namespace MochiBot.Src.Models
{
    /// <summary>
    /// AI桌宠情绪状态枚举
    /// </summary>
    public enum AgentMood
    {
        Happy,      // 开心 - 被夸奖、被摸头后
        Sad,        // 委屈 - 长时间未被关注
        Sleepy,     // 困倦 - 深夜时段
        Touched,    // 感动 - 被摸头后的反应
        Neutral,    // 平静 - 默认状态
        Teasing,    // 调皮 - 毒舌性格下的互动
        Angry,      // 生气 - 被频繁打扰
        Surprised   // 惊讶 - 意外事件触发
    }
}
