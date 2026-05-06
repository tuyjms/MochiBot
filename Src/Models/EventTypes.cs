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

        /// <summary>系统自动事件（定时触发）</summary>
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
    /// 系统自动事件类型
    /// 对应原 AutoEventService 的三个内置事件
    /// </summary>
    public enum SystemAutoEventType
    {
        /// <summary>碎碎念 - 随机触发，表达思念或关心</summary>
        Murmur,

        /// <summary>用眼提醒 - 用户连续使用电脑超阈值</summary>
        EyeRest,

        /// <summary>深夜关怀 - 深夜时段触发</summary>
        LateNight
    }

    /// <summary>
    /// UI交互事件类型
    /// </summary>
    public enum UiInteractionType
    {
        /// <summary>摸摸头</summary>
        Pet,

        /// <summary>点击角色</summary>
        Click,

        /// <summary>拖拽</summary>
        Drag
    }
}
