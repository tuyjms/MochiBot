namespace MochiBot.Src.Core
{
    /// <summary>
    /// 全局常量定义
    /// 集中管理所有协议级标识符，避免魔法字符串散落在各模块中
    /// </summary>
    public static class Constants
    {
        /// <summary>内置自动任务常量</summary>
        public static class BuiltinTasks
        {
            // 任务类型标识（用于 CronTask.TaskType 和事件 Info JSON.type）
            public const string Murmur = "murmur";
            public const string EyeRest = "eye_rest";
            public const string LateNight = "late_night";
            public const string Idle = "idle";
            public const string IdleCheck = "idle_check";

            // 内置任务 ID（用于 CronTask.Id）
            public const string IdMurmur = "builtin:murmur";
            public const string IdEyeRest = "builtin:eye_rest";
            public const string IdLateNight = "builtin:late_night";
            public const string IdIdleCheck = "builtin:idle_check";

            // 显示名称（用于 CronTask.Name 和事件 Info JSON.name）
            public const string NameMurmur = "碎碎念";
            public const string NameEyeRest = "用眼提醒";
            public const string NameLateNight = "深夜关怀";
            public const string NameIdleCheck = "空闲检测";
        }

        /// <summary>工具名称常量</summary>
        public static class Tools
        {
            public const string Timer = "timer";
            public const string Reply = "reply";
            public const string Murmur = "murmur";
            public const string ListPlugins = "list_plugins";
            public const string Cry = "cry";
            public const string Dance = "dance";
            public const string Yawn = "yawn";
            public const string Blush = "blush";
            public const string Stomp = "stomp";
        }

        /// <summary>动作类型常量（Agent actions JSON.type）</summary>
        public static class ActionTypes
        {
            public const string ToolCall = "tool_call";
            public const string PluginCall = "plugin_call";
            public const string McpCall = "mcp_call";
            public const string MoodChange = "mood_change";
            public const string MidtermMemory = "midterm_memory";
            public const string Animation = "animation";
        }

        /// <summary>UI 交互事件类型常量</summary>
        public static class UiInteractionTypes
        {
            public const string Pet = "pet";
        }

        /// <summary>聊天角色常量（用于 ChatMessage.Role 和 OpenAI 消息类型映射）</summary>
        public static class ChatRoles
        {
            public const string System = "system";
            public const string User = "user";
            public const string Assistant = "assistant";
        }

        /// <summary>事件源标识常量（用于事件 Info JSON.source 字段）</summary>
        public static class EventSources
        {
            public const string Tool = "tool";
        }

        /// <summary>渲染器动画类型标识常量（对应 SpriteSheetConfig.Type）</summary>
        public static class SpriteTypes
        {
            public const string Sprite = "sprite";
            public const string Gif = "gif";
            public const string Png = "png";
        }

        /// <summary>角色默认值（当人格配置未设置时的回退值，跨模块共享）</summary>
        public static class CharacterDefaults
        {
            public const string DefaultName = "小琪";
            public const string DefaultAvatarText = "琪";
            public const string DefaultDescription = "温柔可爱，善解人意";
        }

        /// <summary>用户默认值（跨模块共享）</summary>
        public static class UserDefaults
        {
            public const string DefaultUserName = "主人";
        }
    }
}
