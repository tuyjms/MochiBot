using System.Windows;
using System.Windows.Controls;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;

namespace MochiBot.Src.UI.Settings
{
    /// <summary>
    /// Tab 4: 模块参数 — 加载、校验、收集 ModuleSettings
    /// </summary>
    public class ModuleSettingsTabController
    {
        private readonly IConfigReader _configReader;

        // 短期记忆
        private readonly TextBox _stCapacityBox;
        private readonly TextBox _stTrimThresholdBox;
        private readonly ComboBox _stOverflowStrategyBox;
        private readonly TextBox _stSummaryReservedBox;

        // 中期记忆
        private readonly TextBox _mtMaxEntriesBox;
        private readonly TextBox _mtImportanceThresholdBox;
        private readonly TextBox _mtOverflowSampleRateBox;
        private readonly TextBox _mtKeywordScanIntervalBox;
        private readonly TextBox _mtTopKeywordsCountBox;

        // 长期记忆
        private readonly TextBox _ltPromotionIntervalBox;
        private readonly TextBox _ltPromotionThresholdBox;
        private readonly TextBox _ltImmediateThresholdBox;
        private readonly TextBox _ltMaxEntriesBox;
        private readonly TextBox _ltSearchTopNBox;

        public ModuleSettingsTabController(
            IConfigReader configReader,
            TextBox stCapacityBox, TextBox stTrimThresholdBox,
            ComboBox stOverflowStrategyBox, TextBox stSummaryReservedBox,
            TextBox mtMaxEntriesBox, TextBox mtImportanceThresholdBox,
            TextBox mtOverflowSampleRateBox, TextBox mtKeywordScanIntervalBox,
            TextBox mtTopKeywordsCountBox,
            TextBox ltPromotionIntervalBox, TextBox ltPromotionThresholdBox,
            TextBox ltImmediateThresholdBox, TextBox ltMaxEntriesBox,
            TextBox ltSearchTopNBox)
        {
            _configReader = configReader;
            _stCapacityBox = stCapacityBox;
            _stTrimThresholdBox = stTrimThresholdBox;
            _stOverflowStrategyBox = stOverflowStrategyBox;
            _stSummaryReservedBox = stSummaryReservedBox;
            _mtMaxEntriesBox = mtMaxEntriesBox;
            _mtImportanceThresholdBox = mtImportanceThresholdBox;
            _mtOverflowSampleRateBox = mtOverflowSampleRateBox;
            _mtKeywordScanIntervalBox = mtKeywordScanIntervalBox;
            _mtTopKeywordsCountBox = mtTopKeywordsCountBox;
            _ltPromotionIntervalBox = ltPromotionIntervalBox;
            _ltPromotionThresholdBox = ltPromotionThresholdBox;
            _ltImmediateThresholdBox = ltImmediateThresholdBox;
            _ltMaxEntriesBox = ltMaxEntriesBox;
            _ltSearchTopNBox = ltSearchTopNBox;
        }

        /// <summary>加载模块参数到 UI</summary>
        public void Load()
        {
            var ms = _configReader.GetModuleSettings();

            _stCapacityBox.Text = ms.ShortTermMemory_Capacity.ToString();
            _stTrimThresholdBox.Text = ms.ShortTermMemory_TrimThreshold.ToString();
            _stOverflowStrategyBox.ItemsSource = ModuleSettings.ValidOverflowStrategies;
            _stOverflowStrategyBox.SelectedItem = ms.ShortTermMemory_OverflowStrategy;
            _stSummaryReservedBox.Text = ms.ShortTermMemory_SummaryReservedCount.ToString();

            _mtMaxEntriesBox.Text = ms.MidTermMemory_MaxEntries.ToString();
            _mtImportanceThresholdBox.Text = ms.MidTermMemory_ImportanceThreshold.ToString();
            _mtOverflowSampleRateBox.Text = ms.MidTermMemory_OverflowSampleRate.ToString();
            _mtKeywordScanIntervalBox.Text = ms.MidTermMemory_KeywordScanInterval.ToString();
            _mtTopKeywordsCountBox.Text = ms.MidTermMemory_TopKeywordsCount.ToString();

            _ltPromotionIntervalBox.Text = ms.LongTermMemory_PromotionInterval.ToString();
            _ltPromotionThresholdBox.Text = ms.LongTermMemory_PromotionThreshold.ToString();
            _ltImmediateThresholdBox.Text = ms.LongTermMemory_ImmediateThreshold.ToString();
            _ltMaxEntriesBox.Text = ms.LongTermMemory_MaxEntries.ToString();
            _ltSearchTopNBox.Text = ms.LongTermMemory_SearchTopN.ToString();
        }

        /// <summary>从 UI 收集并校验模块参数，失败返回 false</summary>
        public bool TryCollect(out ModuleSettings ms)
        {
            ms = new ModuleSettings();

            if (!int.TryParse(_stCapacityBox.Text, out var cap) || cap < 1)
            { MessageBox.Show("短期记忆容量必须为正整数", "提示"); return false; }
            ms.ShortTermMemory_Capacity = cap;

            if (!int.TryParse(_stTrimThresholdBox.Text, out var trim) || trim < 0)
            { MessageBox.Show("短期记忆修剪阈值必须为非负整数", "提示"); return false; }
            ms.ShortTermMemory_TrimThreshold = trim;

            ms.ShortTermMemory_OverflowStrategy = _stOverflowStrategyBox.SelectedItem?.ToString()
                ?? new ModuleSettings().ShortTermMemory_OverflowStrategy;

            if (!int.TryParse(_stSummaryReservedBox.Text, out var reserved) || reserved < 0)
            { MessageBox.Show("摘要保留数必须为非负整数", "提示"); return false; }
            ms.ShortTermMemory_SummaryReservedCount = reserved;

            if (!int.TryParse(_mtMaxEntriesBox.Text, out var mtMax) || mtMax < 1)
            { MessageBox.Show("中期记忆最大条目必须为正整数", "提示"); return false; }
            ms.MidTermMemory_MaxEntries = mtMax;

            if (!int.TryParse(_mtImportanceThresholdBox.Text, out var mtImp) || mtImp < 0)
            { MessageBox.Show("中期记忆重要性阈值必须为非负整数", "提示"); return false; }
            ms.MidTermMemory_ImportanceThreshold = mtImp;

            if (!double.TryParse(_mtOverflowSampleRateBox.Text, out var mtRate) || mtRate < 0 || mtRate > 1)
            { MessageBox.Show("溢出采样率必须在 0-1 之间", "提示"); return false; }
            ms.MidTermMemory_OverflowSampleRate = mtRate;

            if (!int.TryParse(_mtKeywordScanIntervalBox.Text, out var mtKwInt) || mtKwInt < 1)
            { MessageBox.Show("关键词扫描间隔必须为正整数", "提示"); return false; }
            ms.MidTermMemory_KeywordScanInterval = mtKwInt;

            if (!int.TryParse(_mtTopKeywordsCountBox.Text, out var mtTopKw) || mtTopKw < 1)
            { MessageBox.Show("热门关键词数必须为正整数", "提示"); return false; }
            ms.MidTermMemory_TopKeywordsCount = mtTopKw;

            if (!int.TryParse(_ltPromotionIntervalBox.Text, out var ltInt) || ltInt < 1)
            { MessageBox.Show("长期记忆晋升间隔必须为正整数", "提示"); return false; }
            ms.LongTermMemory_PromotionInterval = ltInt;

            if (!int.TryParse(_ltPromotionThresholdBox.Text, out var ltTh) || ltTh < 0)
            { MessageBox.Show("长期记忆晋升阈值必须为非负整数", "提示"); return false; }
            ms.LongTermMemory_PromotionThreshold = ltTh;

            if (!int.TryParse(_ltImmediateThresholdBox.Text, out var ltImm) || ltImm < 0)
            { MessageBox.Show("长期记忆即时阈值必须为非负整数", "提示"); return false; }
            ms.LongTermMemory_ImmediateThreshold = ltImm;

            if (!int.TryParse(_ltMaxEntriesBox.Text, out var ltMax) || ltMax < 1)
            { MessageBox.Show("长期记忆最大条目必须为正整数", "提示"); return false; }
            ms.LongTermMemory_MaxEntries = ltMax;

            if (!int.TryParse(_ltSearchTopNBox.Text, out var ltSearch) || ltSearch < 1)
            { MessageBox.Show("长期记忆搜索返回数必须为正整数", "提示"); return false; }
            ms.LongTermMemory_SearchTopN = ltSearch;

            return true;
        }
    }
}
