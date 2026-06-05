using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Core.Database.Models;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using MochiBot.Src.UI.Settings;
using EventTrigger = MochiBot.Src.EventModels.EventTrigger;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 设置窗口 — 薄壳层，委托给各 TabController 处理具体逻辑
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigReader _configReader;
        private readonly IEventDispatcher _eventDispatcher;
        private readonly UserConfigRepository? _userConfigRepository;
        private readonly MainWindow _ownerWindow;

        private readonly ProviderTabController _providerTab;
        private readonly PersonalityTabController _personalityTab;
        private readonly ModuleSettingsTabController _moduleTab;

        public SettingsWindow(
            IConfigReader configReader,
            IEventDispatcher eventDispatcher,
            MainWindow owner,
            UserConfigRepository? userConfigRepository = null)
        {
            _configReader = configReader;
            _eventDispatcher = eventDispatcher;
            _ownerWindow = owner;
            _userConfigRepository = userConfigRepository;

            InitializeComponent();

            Owner = owner;

            // 初始化各 Tab 控制器
            _providerTab = new ProviderTabController(_configReader, providersPanel);
            _personalityTab = new PersonalityTabController(
                _configReader,
                personalitySelector, personNameBox, personDescBox, displayModeBox,
                modelProviderBox, modelNameBox, chatModelsList,
                visionProviderBox, visionModelBox, visionModelsList,
                subPersonalitiesGrid, weightSumLabel, activePersonalityBox);
            _moduleTab = new ModuleSettingsTabController(
                _configReader,
                stCapacityBox, stTrimThresholdBox, stOverflowStrategyBox, stSummaryReservedBox,
                mtMaxEntriesBox, mtImportanceThresholdBox, mtOverflowSampleRateBox,
                mtKeywordScanIntervalBox, mtTopKeywordsCountBox,
                ltPromotionIntervalBox, ltPromotionThresholdBox,
                ltImmediateThresholdBox, ltMaxEntriesBox, ltSearchTopNBox);

            // 穿透模式滑条实时预览
            passthroughOpacitySlider.ValueChanged += PassthroughOpacitySlider_ValueChanged;

            LoadCurrentSettings();
        }

        /// <summary>加载当前配置到 UI 控件</summary>
        private void LoadCurrentSettings()
        {
            var appSettings = _configReader.GetAppSettings();

            // === Tab 1: 基础设置 ===
            userNameBox.Text = appSettings.UserName;

            var personalities = _configReader.GetAvailablePersonalities();
            activePersonalityBox.ItemsSource = personalities;
            activePersonalityBox.SelectedItem = appSettings.ActivePersonality;

            // 从数据库加载 UI 相关配置
            if (_userConfigRepository != null)
            {
                try
                {
                    var userConfig = _userConfigRepository.LoadConfigAsync().GetAwaiter().GetResult();
                    murmurEnabledCheck.IsChecked = userConfig.MurmurEnabled;
                    murmurIntervalBox.Text = userConfig.MurmurInterval.ToString();
                }
                catch
                {
                    var defaultMs = new ModuleSettings();
                    murmurEnabledCheck.IsChecked = defaultMs.AutoEvent_MurmurEnabled;
                    murmurIntervalBox.Text = defaultMs.AutoEvent_MurmurInterval.ToString();
                }
            }
            else
            {
                var defaultMs = new ModuleSettings();
                murmurEnabledCheck.IsChecked = defaultMs.AutoEvent_MurmurEnabled;
                murmurIntervalBox.Text = defaultMs.AutoEvent_MurmurInterval.ToString();
            }

            // LLM 行为
            structuredResponseCheck.IsChecked = appSettings.EnableStructuredResponse;
            midTermMemoryOnChatCheck.IsChecked = appSettings.EnableMidTermMemoryOnChat;
            longTermRecallCheck.IsChecked = appSettings.EnableLongTermRecall;
            maxActionsBox.Text = appSettings.MaxActionsPerResponse.ToString();

            // 关闭行为
            closeBehaviorBox.ItemsSource = AppSettings.ValidCloseBehaviors;
            closeBehaviorBox.SelectedItem = appSettings.CloseBehavior;

            // 穿透模式
            passthroughCheck.IsChecked = _ownerWindow.IsPassthrough;
            passthroughOpacitySlider.Value = appSettings.PassthroughOpacity;
            UpdatePassthroughOpacityValue();

            // 日志
            logLevelBox.ItemsSource = AppSettings.ValidLogLevels;
            logLevelBox.SelectedItem = appSettings.LogLevel;
            logToFileCheck.IsChecked = appSettings.LogToFile;
            logToConsoleCheck.IsChecked = appSettings.LogToConsole;

            // 视觉功能
            var moduleSettings = _configReader.GetModuleSettings();
            autoScreenshotOnChatCheck.IsChecked = moduleSettings.Vision_AutoScreenshotOnChat;
            screenshotOnLateNightCheck.IsChecked = moduleSettings.Vision_ScreenshotOnLateNight;
            screenshotOnEyeRestCheck.IsChecked = moduleSettings.Vision_ScreenshotOnEyeRest;

            // === Tab 2~4: 委托给控制器 ===
            _providerTab.Load();
            _personalityTab.Load(appSettings.ActivePersonality);
            _moduleTab.Load();
        }

        // ==================== Tab 3: 人格编辑事件转发 ====================

        private void PersonalitySelector_Changed(object sender, SelectionChangedEventArgs e)
            => _personalityTab.OnPersonalityChanged();

        private void ModelProviderBox_Changed(object sender, SelectionChangedEventArgs e)
            => _personalityTab.OnProviderChanged();

        private void AddChatModel_Click(object sender, RoutedEventArgs e)
            => _personalityTab.AddChatModel();

        private void RemoveChatModel_Click(object sender, RoutedEventArgs e)
            => _personalityTab.RemoveChatModel();

        private void VisionProviderBox_Changed(object sender, SelectionChangedEventArgs e)
            => _personalityTab.OnVisionProviderChanged();

        private void AddVisionModel_Click(object sender, RoutedEventArgs e)
            => _personalityTab.AddVisionModel();

        private void RemoveVisionModel_Click(object sender, RoutedEventArgs e)
            => _personalityTab.RemoveVisionModel();

        private void AddSubPersonality_Click(object sender, RoutedEventArgs e)
            => _personalityTab.AddSubPersonality();

        private void RemoveSubPersonality_Click(object sender, RoutedEventArgs e)
            => _personalityTab.RemoveSubPersonality();

        private void AddPersonality_Click(object sender, RoutedEventArgs e)
            => _personalityTab.AddPersonality();

        private void RemovePersonality_Click(object sender, RoutedEventArgs e)
            => _personalityTab.RemovePersonality();

        // ==================== 桌宠窗口 ====================

        private void UpdatePassthroughOpacityValue()
        {
            passthroughOpacityValue.Text = $"{passthroughOpacitySlider.Value:F1}";
        }

        private void PassthroughOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdatePassthroughOpacityValue();
            if (IsLoaded && _ownerWindow.IsPassthrough)
                _ownerWindow.SetWindowOpacity(passthroughOpacitySlider.Value);
        }

        private void PassthroughCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _ownerWindow.SetPassthrough(passthroughCheck.IsChecked == true);
        }

        // ==================== 调试 ====================

        private void TestScreenshot_Click(object sender, RoutedEventArgs e)
        {
            var path = ScreenshotService.DebugCaptureToFile(_configReader);
            if (path != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("截图失败，请查看日志", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== 保存 ====================

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // ====== 验证 ======
            if (string.IsNullOrWhiteSpace(userNameBox.Text))
            {
                MessageBox.Show("用户名称不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(murmurIntervalBox.Text, out var murmurInterval) || murmurInterval < 1)
            {
                MessageBox.Show("碎碎念间隔必须为大于0的整数", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(maxActionsBox.Text, out var maxActions) || maxActions < 1 || maxActions > AppSettings.MaxActionsUpperBound)
            {
                MessageBox.Show($"最大动作数必须在 1-{AppSettings.MaxActionsUpperBound} 之间", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_moduleTab.TryCollect(out var newModuleSettings))
                return;

            // Tab 1 中的视觉功能设置（不在 ModuleSettingsTabController 管辖范围内）
            newModuleSettings.Vision_AutoScreenshotOnChat = autoScreenshotOnChatCheck.IsChecked == true;
            newModuleSettings.Vision_ScreenshotOnLateNight = screenshotOnLateNightCheck.IsChecked == true;
            newModuleSettings.Vision_ScreenshotOnEyeRest = screenshotOnEyeRestCheck.IsChecked == true;

            if (!_personalityTab.ValidateWeightSum())
                return;

            try
            {
                // 保存前快照（用于变更检测）
                var oldAppSettings = _configReader.GetAppSettings();
                var oldProviders = _configReader.GetAllProviders();
                var oldModuleSettings = _configReader.GetModuleSettings();

                // ====== 1. 保存 AppSettings ======
                var appSettings = _configReader.GetAppSettings();
                appSettings.UserName = userNameBox.Text.Trim();
                appSettings.ActivePersonality = activePersonalityBox.SelectedItem?.ToString() ?? appSettings.ActivePersonality;
                appSettings.EnableStructuredResponse = structuredResponseCheck.IsChecked
                    ?? new AppSettings().EnableStructuredResponse;
                appSettings.EnableMidTermMemoryOnChat = midTermMemoryOnChatCheck.IsChecked
                    ?? new AppSettings().EnableMidTermMemoryOnChat;
                appSettings.EnableLongTermRecall = longTermRecallCheck.IsChecked
                    ?? new AppSettings().EnableLongTermRecall;
                appSettings.MaxActionsPerResponse = maxActions;
                appSettings.LogLevel = logLevelBox.SelectedItem?.ToString()
                    ?? new AppSettings().LogLevel;
                appSettings.LogToFile = logToFileCheck.IsChecked
                    ?? new AppSettings().LogToFile;
                appSettings.LogToConsole = logToConsoleCheck.IsChecked
                    ?? new AppSettings().LogToConsole;
                appSettings.CloseBehavior = closeBehaviorBox.SelectedItem?.ToString()
                    ?? new AppSettings().CloseBehavior;
                appSettings.PassthroughOpacity = passthroughOpacitySlider.Value;
                _configReader.SaveAppSettings(appSettings);

                // ====== 2. 保存数据库配置 ======
                if (_userConfigRepository != null)
                {
                    var userConfig = new UserConfig
                    {
                        Name = appSettings.UserName,
                        Personality = appSettings.ActivePersonality,
                        MurmurEnabled = murmurEnabledCheck.IsChecked ?? true,
                        MurmurInterval = murmurInterval,
                        WindowPosX = (int)_ownerWindow.Left,
                        WindowPosY = (int)_ownerWindow.Top
                    };
                    await _userConfigRepository.SaveConfigAsync(userConfig);
                }

                // ====== 3. 保存 Providers ======
                var newProviders = _providerTab.Collect();
                _configReader.SaveProviders(newProviders);

                // ====== 4. 保存 ModuleSettings ======
                _configReader.SaveModuleSettings(newModuleSettings);

                // ====== 5. 保存人格配置 ======
                _personalityTab.SaveCurrent();

                // ====== 变更检测 ======
                var changedItems = new List<string>();

                // AppSettings 字段变更
                if (oldAppSettings.UserName != appSettings.UserName) changedItems.Add("UserName");
                if (oldAppSettings.ActivePersonality != appSettings.ActivePersonality) changedItems.Add("ActivePersonality");
                if (oldAppSettings.EnableStructuredResponse != appSettings.EnableStructuredResponse) changedItems.Add("EnableStructuredResponse");
                if (oldAppSettings.EnableMidTermMemoryOnChat != appSettings.EnableMidTermMemoryOnChat) changedItems.Add("EnableMidTermMemoryOnChat");
                if (oldAppSettings.EnableLongTermRecall != appSettings.EnableLongTermRecall) changedItems.Add("EnableLongTermRecall");
                if (oldAppSettings.MaxActionsPerResponse != appSettings.MaxActionsPerResponse) changedItems.Add("MaxActionsPerResponse");

                // Provider 配置变更（序列化比较）
                if (System.Text.Json.JsonSerializer.Serialize(oldProviders) != System.Text.Json.JsonSerializer.Serialize(newProviders))
                    changedItems.Add("ProviderConfig");

                // 模块参数变更（序列化比较）
                if (System.Text.Json.JsonSerializer.Serialize(oldModuleSettings) != System.Text.Json.JsonSerializer.Serialize(newModuleSettings))
                    changedItems.Add("ModuleSettings");

                // 人格配置变更（SaveCurrent 总是写文件，保守标记为已变更）
                changedItems.Add("PersonalityConfig");

                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.ConfigChanged,
                    Trigger = EventTrigger.User,
                    Info = JsonSerializer.Serialize(new { changedItems })
                });

                _configReader.Logger.Info("[Settings] 配置已保存并应用热重载");

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error("[Settings] 保存配置失败", ex);
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show("确定要退出桌宠吗？", "确认退出",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _configReader.Logger.Info("[Settings] 用户主动退出桌宠");
            Application.Current.Shutdown();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
