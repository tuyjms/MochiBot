namespace catgirlwindow.Src.Models
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
        Mcp
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
    /// 系统自动事件类型，只需在事件信息中说明就行
    /// UI交互事件类型也是只需在事件信息说明
    /// </summary>
}
