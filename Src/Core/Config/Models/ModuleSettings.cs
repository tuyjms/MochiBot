namespace catgirlwindow.Src.Core.Config.Models
{
    /// <summary>
    /// 模块参数配置
    /// </summary>
    public class ModuleSettings
    {
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
    }
}
