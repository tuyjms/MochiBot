namespace MochiBot.Src.EventModels
{
    /// <summary>
    /// 事件分类枚举
    /// 用于事件调度器对事件进行分类
    /// </summary>
    public enum EventCategory
    {
        /// <summary>用户输入事件（聊天消息）</summary>
        UserInput,

        /// <summary>系统自动事件（定时触发，比如桌宠饿了之类的提示）</summary>
        SystemAuto,

        /// <summary>工具执行结果事件</summary>
        ToolResult,

        /// <summary>情绪变化事件</summary>
        MoodChange,

        /// <summary>UI交互事件（摸摸、点击等）</summary>
        UiInteraction,

        /// <summary>插件事件</summary>
        Plugin,

        /// <summary>MCP事件</summary>
        Mcp,

        /// <summary>配置更新请求事件（UI → ConfigReader）</summary>
        ConfigUpdate,

        /// <summary>配置已变更事件（ConfigReader → 各模块，触发热重载）</summary>
        ConfigChanged,

        /// <summary>模块状态变更事件（Agent 状态机切换时发布）</summary>
        ModuleState
    }

    /// <summary>
    /// 事件触发者枚举
    /// </summary>
    public enum EventTrigger
    {
        /// <summary>用户触发</summary>
        User,

        /// <summary>系统自动触发</summary>
        System,

        /// <summary>LLM触发</summary>
        Llm,

        /// <summary>工具触发</summary>
        Tool,

        /// <summary>插件触发</summary>
        Plugin,

        /// <summary>MCP服务触发</summary>
        Mcp
    }

    /// <summary>
    /// 事件数据结构
    /// 格式：（事件类型枚举值，事件触发者枚举，事件信息string）
    /// </summary>
    public class EventData
    {
        /// <summary>事件分类（枚举值）</summary>
        public EventCategory Category { get; set; }

        /// <summary>事件触发者（枚举值）</summary>
        public EventTrigger Trigger { get; set; }

        /// <summary>事件信息（JSON字符串，包含具体事件类型和附加数据）</summary>
        public string Info { get; set; } = string.Empty;

        /// <summary>事件创建时间</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 情绪事件类型常量
    /// 用于 ChangeMoodByEvent 和 DetectAndTriggerMoodEvent 的事件类型字符串
    /// </summary>
    public static class MoodEventTypes
    {
        public const string LateNight = "LateNight";
        public const string Sleepy = "Sleepy";
        public const string LongWork = "LongWork";
        public const string Idle = "Idle";
        public const string Active = "Active";
        public const string Pet = "Pet";
        public const string Compliment = "Compliment";
        public const string Angry = "Angry";
    }
}
