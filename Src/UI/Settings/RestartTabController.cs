using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Core.Events;

namespace MochiBot.Src.UI.Settings
{
    /// <summary>
    /// Tab 5: 需重启 — 管理修改后需要重启才能生效的配置项
    /// 包含：显示模式、摘要保留数、定时任务
    /// </summary>
    public class RestartTabController
    {
        private readonly IConfigReader _configReader;

        // 显示模式
        private readonly ComboBox _displayModeBox;

        // 摘要保留数
        private readonly TextBox _stSummaryReservedBox;

        // 定时任务
        private readonly DataGrid _cronTasksGrid;
        private readonly ObservableCollection<CronTask> _cronTasks = new();

        // 变更检测快照
        private string _snapshotDisplayMode = "Gif";
        private int _snapshotSummaryReserved;
        private string _snapshotCronTasksJson = "[]";

        public RestartTabController(
            IConfigReader configReader,
            ComboBox displayModeBox,
            TextBox stSummaryReservedBox,
            DataGrid cronTasksGrid)
        {
            _configReader = configReader;
            _displayModeBox = displayModeBox;
            _stSummaryReservedBox = stSummaryReservedBox;
            _cronTasksGrid = cronTasksGrid;

            _cronTasksGrid.ItemsSource = _cronTasks;
        }

        /// <summary>加载需重启的配置项到 UI</summary>
        /// <param name="personality">当前激活的人格配置（用于读取 DisplayMode）</param>
        public void Load(PersonalityConfig? personality)
        {
            // 显示模式
            var displayMode = personality?.DisplayMode ?? "Gif";
            _displayModeBox.SelectedIndex = displayMode == "Vrm" ? 1 : 0;
            _snapshotDisplayMode = displayMode;

            // 摘要保留数
            var ms = _configReader.GetModuleSettings();
            _stSummaryReservedBox.Text = ms.ShortTermMemory_SummaryReservedCount.ToString();
            _snapshotSummaryReserved = ms.ShortTermMemory_SummaryReservedCount;

            // 定时任务
            _cronTasks.Clear();
            var tasks = _configReader.GetCronTasks();
            foreach (var task in tasks)
                _cronTasks.Add(task);
            _snapshotCronTasksJson = SerializeCronTasks(_cronTasks.ToList());
        }

        /// <summary>
        /// 收集 UI 值并写入对应的配置模型
        /// </summary>
        /// <param name="personality">要更新 DisplayMode 的人格配置</param>
        /// <param name="moduleSettings">要更新 SummaryReservedCount 的模块配置</param>
        /// <returns>校验是否通过</returns>
        public bool TryCollect(PersonalityConfig personality, ModuleSettings moduleSettings)
        {
            // 显示模式
            var displayMode = _displayModeBox.SelectedIndex == 1 ? "Vrm" : "Gif";
            personality.DisplayMode = displayMode;

            // 摘要保留数
            if (!int.TryParse(_stSummaryReservedBox.Text, out var reserved) || reserved < 0)
            {
                MessageBox.Show("摘要保留数必须为非负整数", "提示");
                return false;
            }
            moduleSettings.ShortTermMemory_SummaryReservedCount = reserved;

            return true;
        }

        /// <summary>获取当前定时任务列表</summary>
        public List<CronTask> GetCronTasks() => _cronTasks.ToList();

        /// <summary>检测是否有变更（用于判断是否需要重启提示）</summary>
        public bool HasChanges()
        {
            var currentDisplayMode = _displayModeBox.SelectedIndex == 1 ? "Vrm" : "Gif";
            if (currentDisplayMode != _snapshotDisplayMode) return true;

            if (int.TryParse(_stSummaryReservedBox.Text, out var reserved)
                && reserved != _snapshotSummaryReserved) return true;

            var currentCronJson = SerializeCronTasks(_cronTasks.ToList());
            if (currentCronJson != _snapshotCronTasksJson) return true;

            return false;
        }

        /// <summary>获取变更项的中文描述列表（用于重启提示）</summary>
        public List<string> GetChangedDescriptions()
        {
            var result = new List<string>();

            var currentDisplayMode = _displayModeBox.SelectedIndex == 1 ? "Vrm" : "Gif";
            if (currentDisplayMode != _snapshotDisplayMode)
                result.Add("显示模式");

            if (int.TryParse(_stSummaryReservedBox.Text, out var reserved)
                && reserved != _snapshotSummaryReserved)
                result.Add("摘要保留数");

            var currentCronJson = SerializeCronTasks(_cronTasks.ToList());
            if (currentCronJson != _snapshotCronTasksJson)
                result.Add("定时任务");

            return result;
        }

        private static string SerializeCronTasks(List<CronTask> tasks)
        {
            return System.Text.Json.JsonSerializer.Serialize(tasks);
        }
    }
}
