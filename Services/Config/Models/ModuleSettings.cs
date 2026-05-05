namespace catgirlwindow.Services.Config.Models;

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

    // ========== 自动事件 ==========
    public int AutoEvent_MurmurInterval { get; set; } = 30;
    public bool AutoEvent_MurmurEnabled { get; set; } = true;
    public int AutoEvent_EyeRestInterval { get; set; } = 120;
    public string AutoEvent_LateNightStart { get; set; } = "23:00";
    public string AutoEvent_LateNightEnd { get; set; } = "06:00";
    public int AutoEvent_IdleThreshold { get; set; } = 5;
}
