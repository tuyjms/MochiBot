namespace MochiBot.Src.Core.Config.Models
{
    /// <summary>
    /// 模块参数配置
    /// </summary>
    public class ModuleSettings
    {
        /// <summary>合法溢出策略选项</summary>
        public static readonly string[] ValidOverflowStrategies = { "Truncate", "Summarize" };

        /// <summary>碎碎念权重上限 (0-100)</summary>
        public const int MaxMurmurWeight = 100;

        // ========== 短期记忆 ==========
        public int ShortTermMemory_Capacity { get; set; } = 50;
        public int ShortTermMemory_TrimThreshold { get; set; } = 40;
        public string ShortTermMemory_OverflowStrategy { get; set; } = "Truncate";
        public int ShortTermMemory_SummaryReservedCount { get; set; } = 10;

        // ========== 中期记忆 ==========
        public int MidTermMemory_MaxEntries { get; set; } = 500;
        public int MidTermMemory_ImportanceThreshold { get; set; } = 30;
        public double MidTermMemory_OverflowSampleRate { get; set; } = 0.3;
        public int MidTermMemory_KeywordScanInterval { get; set; } = 30;
        public int MidTermMemory_TopKeywordsCount { get; set; } = 10;

        // ========== 长期记忆 ==========
        public int LongTermMemory_PromotionInterval { get; set; } = 60;
        public int LongTermMemory_PromotionThreshold { get; set; } = 60;
        public int LongTermMemory_ImmediateThreshold { get; set; } = 80;
        public int LongTermMemory_MaxEntries { get; set; } = 10000;
        public int LongTermMemory_SearchTopN { get; set; } = 5;

        // ========== 内置任务参数（由 BuiltinTaskInitializer 读取） ==========

        /// <summary>碎碎念：触发间隔（分钟）</summary>
        public int AutoEvent_MurmurInterval { get; set; } = 30;
        /// <summary>碎碎念：是否启用</summary>
        public bool AutoEvent_MurmurEnabled { get; set; } = true;
        /// <summary>碎碎念：触发权重（0-100，每次检查时随机触发的概率）</summary>
        public int AutoEvent_MurmurWeight { get; set; } = 30;

        /// <summary>用眼提醒：无操作阈值（分钟）</summary>
        public int AutoEvent_EyeRestInterval { get; set; } = 120;

        /// <summary>深夜关怀：基准开始时间</summary>
        public string AutoEvent_LateNightStart { get; set; } = "23:00";
        /// <summary>深夜关怀：基准结束时间</summary>
        public string AutoEvent_LateNightEnd { get; set; } = "06:00";
        /// <summary>深夜关怀：偏移范围最小值（分钟）</summary>
        public int AutoEvent_LateNightOffsetMin { get; set; } = -30;
        /// <summary>深夜关怀：偏移范围最大值（分钟）</summary>
        public int AutoEvent_LateNightOffsetMax { get; set; } = 30;

        /// <summary>空闲检测：无操作阈值（分钟）</summary>
        public int AutoEvent_IdleThreshold { get; set; } = 5;


        // ========== 内置文本模板 ==========

        /// <summary>碎碎念文本列表（Agent 随机选取，不调用 LLM）</summary>
        public List<string> MurmurTexts { get; set; } = new()
        {
            "那个…你在忙吗？我、我只是想你了…",
            "唔…今天有没有好好吃饭呀？",
            "嘿嘿，突然好想抱抱你～",
            "那个…你认真工作的样子好帅…",
            "呜…你都不理我，我有点难过…",
            "今天天气不错呢，要不要出去走走？",
            "你身上有股让人安心的味道…",
            "那个…我刚刚梦到你了…",
            "嘿嘿，看到你就很开心～",
            "唔…你要记得多喝水哦…",
            "那个…你什么时候才来陪我呀？",
            "今天有没有想我呀？一点点也好…",
            "你眼睛有点红红的，是不是又熬夜了…",
            "那个…我可以一直待在你身边吗？",
            "嘿嘿，你今天的笑容特别好看～"
        };

        // ========== 视觉功能 ==========

        /// <summary>已阅读截图声明（启动时检查，未阅读则弹窗）</summary>
        public bool Vision_ScreenshotConsent { get; set; } = false;

        /// <summary>聊天消息时是否自动截图</summary>
        public bool Vision_AutoScreenshotOnChat { get; set; } = true;

        /// <summary>深夜关怀事件时是否截图</summary>
        public bool Vision_ScreenshotOnLateNight { get; set; } = true;

        /// <summary>用眼提醒事件时是否截图</summary>
        public bool Vision_ScreenshotOnEyeRest { get; set; } = false;
    }
}
