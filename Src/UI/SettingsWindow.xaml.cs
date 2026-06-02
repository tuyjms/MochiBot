using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Database.Models;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using EventTrigger = MochiBot.Src.EventModels.EventTrigger;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 设置窗口
    /// 允许用户修改配置并保存，保存后发布 ConfigChanged 事件触发热重载
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigReader _configReader;
        private readonly IEventDispatcher _eventDispatcher;
        private readonly UserConfigRepository? _userConfigRepository;
        private readonly Window _ownerWindow;

        // 人格编辑用的数据源
        private ObservableCollection<string> _chatModels = new();
        private ObservableCollection<SubPersonalityViewModel> _subPersonalities = new();

        // 缓存所有人格配置，保存时写回
        private Dictionary<string, PersonalityConfig> _personalityCache = new();
        private string? _currentPersonalityName;

        public SettingsWindow(
            IConfigReader configReader,
            IEventDispatcher eventDispatcher,
            Window owner,
            UserConfigRepository? userConfigRepository = null)
        {
            _configReader = configReader;
            _eventDispatcher = eventDispatcher;
            _ownerWindow = owner;
            _userConfigRepository = userConfigRepository;

            InitializeComponent();

            Owner = owner;

            // 在 InitializeComponent 之后绑定事件，避免 XAML 加载时触发
            opacitySlider.ValueChanged += OpacitySlider_ValueChanged;
            passthroughOpacitySlider.ValueChanged += PassthroughOpacitySlider_ValueChanged;
            _subPersonalities.CollectionChanged += (_, _) => UpdateWeightSum();

            LoadCurrentSettings();
        }

        /// <summary>加载当前配置到 UI 控件</summary>
        private void LoadCurrentSettings()
        {
            var appSettings = _configReader.GetAppSettings();

            // === Tab 1: 基础设置 ===
            userNameBox.Text = appSettings.UserName;

            // 激活人格下拉
            var personalities = _configReader.GetAvailablePersonalities();
            activePersonalityBox.ItemsSource = personalities;
            activePersonalityBox.SelectedItem = appSettings.ActivePersonality;

            // 从数据库加载 UI 相关配置
            if (_userConfigRepository != null)
            {
                try
                {
                    var userConfig = _userConfigRepository.LoadConfigAsync().GetAwaiter().GetResult();
                    opacitySlider.Value = userConfig.Opacity;
                    murmurEnabledCheck.IsChecked = userConfig.MurmurEnabled;
                    murmurIntervalBox.Text = userConfig.MurmurInterval.ToString();
                }
                catch
                {
                    var defaultMs = new ModuleSettings();
                    opacitySlider.Value = 1.0;
                    murmurEnabledCheck.IsChecked = defaultMs.AutoEvent_MurmurEnabled;
                    murmurIntervalBox.Text = defaultMs.AutoEvent_MurmurInterval.ToString();
                }
            }
            else
            {
                var defaultMs = new ModuleSettings();
                opacitySlider.Value = 1.0;
                murmurEnabledCheck.IsChecked = defaultMs.AutoEvent_MurmurEnabled;
                murmurIntervalBox.Text = defaultMs.AutoEvent_MurmurInterval.ToString();
            }
            UpdateOpacityValue();

            // LLM 行为
            structuredResponseCheck.IsChecked = appSettings.EnableStructuredResponse;
            midTermMemoryOnChatCheck.IsChecked = appSettings.EnableMidTermMemoryOnChat;
            longTermRecallCheck.IsChecked = appSettings.EnableLongTermRecall;
            maxActionsBox.Text = appSettings.MaxActionsPerResponse.ToString();

            // 关闭行为
            closeBehaviorBox.ItemsSource = AppSettings.ValidCloseBehaviors;
            closeBehaviorBox.SelectedItem = appSettings.CloseBehavior;

            // 穿透透明度
            passthroughOpacitySlider.Value = appSettings.PassthroughOpacity;
            UpdatePassthroughOpacityValue();

            // 日志
            logLevelBox.ItemsSource = AppSettings.ValidLogLevels;
            logLevelBox.SelectedItem = appSettings.LogLevel;
            logToFileCheck.IsChecked = appSettings.LogToFile;
            logToConsoleCheck.IsChecked = appSettings.LogToConsole;

            // === Tab 2: LLM 提供商 ===
            LoadProviders();

            // === Tab 3: 人格编辑 ===
            LoadPersonalitiesTab(appSettings.ActivePersonality);

            // === Tab 4: 模块参数 ===
            LoadModuleSettings();
        }

        // ==================== Tab 2: LLM 提供商 ====================

        private void LoadProviders()
        {
            providersPanel.Children.Clear();
            var providers = _configReader.GetAllProviders();

            foreach (var (name, config) in providers)
            {
                var expander = new Expander
                {
                    Header = name,
                    Margin = new Thickness(0, 0, 0, 8),
                    IsExpanded = true
                };

                var panel = new StackPanel { Margin = new Thickness(16, 4, 0, 4) };

                // ApiKey
                panel.Children.Add(new TextBlock { Text = "API Key", FontSize = 13, Margin = new Thickness(0, 2, 0, 2) });
                var apiKeyBox = new PasswordBox
                {
                    Height = 26,
                    FontSize = 13,
                    Padding = new Thickness(4, 0, 4, 0),
                    Tag = $"{name}:ApiKey",
                    Password = config.ApiKey
                };
                panel.Children.Add(apiKeyBox);

                // BaseUrl
                panel.Children.Add(new TextBlock { Text = "Base URL", FontSize = 13, Margin = new Thickness(0, 6, 0, 2) });
                var baseUrlBox = new TextBox
                {
                    Height = 26,
                    FontSize = 13,
                    Padding = new Thickness(4, 0, 4, 0),
                    Tag = $"{name}:BaseUrl",
                    Text = config.BaseUrl
                };
                panel.Children.Add(baseUrlBox);

                // ContextLimit
                panel.Children.Add(new TextBlock { Text = "上下文限制 (tokens)", FontSize = 13, Margin = new Thickness(0, 6, 0, 2) });
                var ctxLimitBox = new TextBox
                {
                    Height = 26,
                    FontSize = 13,
                    Padding = new Thickness(4, 0, 4, 0),
                    TextAlignment = TextAlignment.Center,
                    Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Tag = $"{name}:ContextLimit",
                    Text = config.ContextLimit.ToString()
                };
                panel.Children.Add(ctxLimitBox);

                expander.Content = panel;
                providersPanel.Children.Add(expander);
            }
        }

        private Dictionary<string, ProviderConfig> CollectProviders()
        {
            var result = new Dictionary<string, ProviderConfig>();
            foreach (var child in providersPanel.Children)
            {
                if (child is not Expander expander || expander.Content is not StackPanel panel)
                    continue;

                var providerName = expander.Header?.ToString() ?? "";
                if (string.IsNullOrEmpty(providerName)) continue;

                var pc = new ProviderConfig();
                foreach (UIElement ctrl in panel.Children)
                {
                    if (ctrl is not FrameworkElement fe) continue;
                    var tag = GetTag(fe);
                    if (tag == null) continue;
                    var (_, field) = tag.Value;
                    if (field == "ApiKey" && ctrl is PasswordBox pb)
                        pc.ApiKey = pb.Password;
                    else if (field == "BaseUrl" && ctrl is TextBox tb)
                        pc.BaseUrl = tb.Text;
                    else if (field == "ContextLimit" && ctrl is TextBox tbc && int.TryParse(tbc.Text, out var ctx))
                        pc.ContextLimit = ctx;
                }
                result[providerName] = pc;
            }
            return result;
        }

        private static (string provider, string field)? GetTag(FrameworkElement ctrl)
        {
            var tag = ctrl.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return null;
            var parts = tag.Split(':');
            if (parts.Length != 2) return null;
            return (parts[0], parts[1]);
        }

        // ==================== Tab 3: 人格编辑 ====================

        private void LoadPersonalitiesTab(string? selectedName)
        {
            // 人格选择下拉
            var personalities = _configReader.GetAvailablePersonalities();
            personalitySelector.ItemsSource = personalities;

            // 缓存所有人格配置
            _personalityCache.Clear();
            foreach (var name in personalities)
            {
                var config = _configReader.LoadPersonality(name);
                if (config != null)
                    _personalityCache[name] = config;
            }

            if (!string.IsNullOrEmpty(selectedName) && personalities.Contains(selectedName))
                personalitySelector.SelectedItem = selectedName;
            else if (personalities.Count > 0)
                personalitySelector.SelectedIndex = 0;
        }

        private void PersonalitySelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (personalitySelector.SelectedItem is not string name) return;
            _currentPersonalityName = name;

            if (_personalityCache.TryGetValue(name, out var config))
            {
                personNameBox.Text = config.Name;
                personDescBox.Text = config.Description;

                _chatModels = new ObservableCollection<string>(config.ChatModels ?? new List<string>());
                chatModelsList.ItemsSource = _chatModels;

                _subPersonalities = new ObservableCollection<SubPersonalityViewModel>(
                    (config.Personalities ?? new List<SubPersonality>()).Select(s =>
                        new SubPersonalityViewModel { Name = s.Name, Description = s.Description, Weight = s.Weight }));
                _subPersonalities.CollectionChanged += (_, _) => UpdateWeightSum();
                subPersonalitiesGrid.ItemsSource = _subPersonalities;

                UpdateWeightSum();
            }
        }

        private void AddChatModel_Click(object sender, RoutedEventArgs e)
        {
            var input = ShowInputDialog("输入模型名称（格式: Provider/Model）", "添加聊天模型");
            if (!string.IsNullOrWhiteSpace(input))
                _chatModels.Add(input.Trim());
        }

        private void RemoveChatModel_Click(object sender, RoutedEventArgs e)
        {
            if (chatModelsList.SelectedItem is string selected)
                _chatModels.Remove(selected);
        }

        private void AddSubPersonality_Click(object sender, RoutedEventArgs e)
        {
            _subPersonalities.Add(new SubPersonalityViewModel { Name = string.Empty, Description = string.Empty, Weight = 0 });
        }

        private void RemoveSubPersonality_Click(object sender, RoutedEventArgs e)
        {
            if (subPersonalitiesGrid.SelectedItem is SubPersonalityViewModel selected)
                _subPersonalities.Remove(selected);
        }

        private void AddPersonality_Click(object sender, RoutedEventArgs e)
        {
            var input = ShowInputDialog("输入人格名称（中文、英文、数字、下划线）", "新增人格");
            if (string.IsNullOrWhiteSpace(input)) return;

            var name = input.Trim();
            if (!ConfigReader.IsValidPersonalityName(name))
            {
                MessageBox.Show("人格名称无效，只能包含中文、英文、数字、下划线，且不能以数字开头", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_personalityCache.ContainsKey(name))
            {
                MessageBox.Show($"人格 \"{name}\" 已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var config = new PersonalityConfig { Name = name };
                _configReader.SavePersonality(name, config);
                _personalityCache[name] = config;

                RefreshPersonalitySelector();
                personalitySelector.SelectedItem = name;
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error("[Settings] 新增人格失败", ex);
                MessageBox.Show($"新增人格失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemovePersonality_Click(object sender, RoutedEventArgs e)
        {
            if (personalitySelector.SelectedItem is not string name) return;

            var activeName = activePersonalityBox.SelectedItem?.ToString();
            if (name == activeName)
            {
                MessageBox.Show("不能删除当前激活的人格，请先切换到其他人格", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show($"确定要删除人格 \"{name}\" 吗？此操作不可撤销。", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                _configReader.DeletePersonality(name);
                _personalityCache.Remove(name);

                RefreshPersonalitySelector();

                if (personalitySelector.Items.Count > 0)
                    personalitySelector.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error("[Settings] 删除人格失败", ex);
                MessageBox.Show($"删除人格失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshPersonalitySelector()
        {
            var personalities = _configReader.GetAvailablePersonalities();
            personalitySelector.ItemsSource = personalities;
        }

        private void UpdateWeightSum()
        {
            var required = PersonalityConfig.SubPersonalityWeightSum;
            var sum = _subPersonalities.Sum(s => s.Weight);
            weightSumLabel.Text = $"权重总和: {sum}" + (sum != required ? $"（应为 {required}）" : "");
            weightSumLabel.Foreground = sum == required
                ? System.Windows.Media.Brushes.Gray
                : System.Windows.Media.Brushes.Red;
        }

        // ==================== Tab 4: 模块参数 ====================

        private void LoadModuleSettings()
        {
            var ms = _configReader.GetModuleSettings();

            // 短期记忆
            stCapacityBox.Text = ms.ShortTermMemory_Capacity.ToString();
            stTrimThresholdBox.Text = ms.ShortTermMemory_TrimThreshold.ToString();
            stOverflowStrategyBox.ItemsSource = ModuleSettings.ValidOverflowStrategies;
            stOverflowStrategyBox.SelectedItem = ms.ShortTermMemory_OverflowStrategy;
            stSummaryReservedBox.Text = ms.ShortTermMemory_SummaryReservedCount.ToString();

            // 中期记忆
            mtMaxEntriesBox.Text = ms.MidTermMemory_MaxEntries.ToString();
            mtImportanceThresholdBox.Text = ms.MidTermMemory_ImportanceThreshold.ToString();
            mtOverflowSampleRateBox.Text = ms.MidTermMemory_OverflowSampleRate.ToString();
            mtKeywordScanIntervalBox.Text = ms.MidTermMemory_KeywordScanInterval.ToString();
            mtTopKeywordsCountBox.Text = ms.MidTermMemory_TopKeywordsCount.ToString();

            // 长期记忆
            ltPromotionIntervalBox.Text = ms.LongTermMemory_PromotionInterval.ToString();
            ltPromotionThresholdBox.Text = ms.LongTermMemory_PromotionThreshold.ToString();
            ltImmediateThresholdBox.Text = ms.LongTermMemory_ImmediateThreshold.ToString();
            ltMaxEntriesBox.Text = ms.LongTermMemory_MaxEntries.ToString();
            ltSearchTopNBox.Text = ms.LongTermMemory_SearchTopN.ToString();
        }

        private bool TryCollectModuleSettings(out ModuleSettings ms)
        {
            ms = new ModuleSettings();

            // 短期记忆
            if (!int.TryParse(stCapacityBox.Text, out var cap) || cap < 1)
            { MessageBox.Show("短期记忆容量必须为正整数", "提示"); return false; }
            ms.ShortTermMemory_Capacity = cap;

            if (!int.TryParse(stTrimThresholdBox.Text, out var trim) || trim < 0)
            { MessageBox.Show("短期记忆修剪阈值必须为非负整数", "提示"); return false; }
            ms.ShortTermMemory_TrimThreshold = trim;

            ms.ShortTermMemory_OverflowStrategy = stOverflowStrategyBox.SelectedItem?.ToString()
                ?? new ModuleSettings().ShortTermMemory_OverflowStrategy;

            if (!int.TryParse(stSummaryReservedBox.Text, out var reserved) || reserved < 0)
            { MessageBox.Show("摘要保留数必须为非负整数", "提示"); return false; }
            ms.ShortTermMemory_SummaryReservedCount = reserved;

            // 中期记忆
            if (!int.TryParse(mtMaxEntriesBox.Text, out var mtMax) || mtMax < 1)
            { MessageBox.Show("中期记忆最大条目必须为正整数", "提示"); return false; }
            ms.MidTermMemory_MaxEntries = mtMax;

            if (!int.TryParse(mtImportanceThresholdBox.Text, out var mtImp) || mtImp < 0)
            { MessageBox.Show("中期记忆重要性阈值必须为非负整数", "提示"); return false; }
            ms.MidTermMemory_ImportanceThreshold = mtImp;

            if (!double.TryParse(mtOverflowSampleRateBox.Text, out var mtRate) || mtRate < 0 || mtRate > 1)
            { MessageBox.Show("溢出采样率必须在 0-1 之间", "提示"); return false; }
            ms.MidTermMemory_OverflowSampleRate = mtRate;

            if (!int.TryParse(mtKeywordScanIntervalBox.Text, out var mtKwInt) || mtKwInt < 1)
            { MessageBox.Show("关键词扫描间隔必须为正整数", "提示"); return false; }
            ms.MidTermMemory_KeywordScanInterval = mtKwInt;

            if (!int.TryParse(mtTopKeywordsCountBox.Text, out var mtTopKw) || mtTopKw < 1)
            { MessageBox.Show("热门关键词数必须为正整数", "提示"); return false; }
            ms.MidTermMemory_TopKeywordsCount = mtTopKw;

            // 长期记忆
            if (!int.TryParse(ltPromotionIntervalBox.Text, out var ltInt) || ltInt < 1)
            { MessageBox.Show("长期记忆晋升间隔必须为正整数", "提示"); return false; }
            ms.LongTermMemory_PromotionInterval = ltInt;

            if (!int.TryParse(ltPromotionThresholdBox.Text, out var ltTh) || ltTh < 0)
            { MessageBox.Show("长期记忆晋升阈值必须为非负整数", "提示"); return false; }
            ms.LongTermMemory_PromotionThreshold = ltTh;

            if (!int.TryParse(ltImmediateThresholdBox.Text, out var ltImm) || ltImm < 0)
            { MessageBox.Show("长期记忆即时阈值必须为非负整数", "提示"); return false; }
            ms.LongTermMemory_ImmediateThreshold = ltImm;

            if (!int.TryParse(ltMaxEntriesBox.Text, out var ltMax) || ltMax < 1)
            { MessageBox.Show("长期记忆最大条目必须为正整数", "提示"); return false; }
            ms.LongTermMemory_MaxEntries = ltMax;

            if (!int.TryParse(ltSearchTopNBox.Text, out var ltSearch) || ltSearch < 1)
            { MessageBox.Show("长期记忆搜索返回数必须为正整数", "提示"); return false; }
            ms.LongTermMemory_SearchTopN = ltSearch;

            return true;
        }

        // ==================== 透明度 ====================

        private void UpdateOpacityValue()
        {
            opacityValue.Text = $"{opacitySlider.Value:F1}";
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateOpacityValue();
        }

        private void UpdatePassthroughOpacityValue()
        {
            passthroughOpacityValue.Text = $"{passthroughOpacitySlider.Value:F1}";
        }

        private void PassthroughOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdatePassthroughOpacityValue();
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

            // 模块参数验证
            if (!TryCollectModuleSettings(out var newModuleSettings))
                return;

            // 子人格权重校验
            if (_subPersonalities.Count > 0)
            {
                var weightSum = _subPersonalities.Sum(s => s.Weight);
                if (weightSum != PersonalityConfig.SubPersonalityWeightSum)
                {
                    MessageBox.Show($"子人格权重总和为 {weightSum}，应为 {PersonalityConfig.SubPersonalityWeightSum}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            try
            {
                var oldUserName = _configReader.GetAppSettings().UserName;

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
                        Opacity = opacitySlider.Value,
                        MurmurEnabled = murmurEnabledCheck.IsChecked ?? true,
                        MurmurInterval = murmurInterval,
                        WindowPosX = (int)_ownerWindow.Left,
                        WindowPosY = (int)_ownerWindow.Top
                    };
                    await _userConfigRepository.SaveConfigAsync(userConfig);
                }

                _ownerWindow.Opacity = opacitySlider.Value;

                // ====== 3. 保存 Providers ======
                var providers = CollectProviders();
                _configReader.SaveProviders(providers);

                // ====== 4. 保存 ModuleSettings ======
                _configReader.SaveModuleSettings(newModuleSettings);

                // ====== 5. 保存人格配置 ======
                SaveCurrentPersonality();

                // ====== 发布事件 ======
                var changedItems = new List<string>();
                if (oldUserName != appSettings.UserName) changedItems.Add("UserName");

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

        private void SaveCurrentPersonality()
        {
            if (string.IsNullOrEmpty(_currentPersonalityName)) return;
            if (!_personalityCache.TryGetValue(_currentPersonalityName, out var config)) return;

            config.Name = personNameBox.Text.Trim();
            config.Description = personDescBox.Text;

            config.ChatModels = _chatModels.ToList();

            config.Personalities = _subPersonalities.Select(s => new SubPersonality
            {
                Name = s.Name,
                Description = s.Description,
                Weight = s.Weight
            }).ToList();

            _configReader.SavePersonality(_currentPersonalityName, config);
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

        /// <summary>显示带 Owner 的输入对话框，返回用户输入或 null</summary>
        private string? ShowInputDialog(string prompt, string title)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 360,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
            };

            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = prompt, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });

            var textBox = new TextBox { Height = 28, FontSize = 14, Padding = new Thickness(4, 0, 4, 0) };
            panel.Children.Add(textBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var okBtn = new Button { Content = "确定", Width = 70, Height = 28, FontSize = 13, IsDefault = true };
            var cancelBtn = new Button { Content = "取消", Width = 70, Height = 28, FontSize = 13, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);

            dialog.Content = panel;

            okBtn.Click += (_, _) => { dialog.DialogResult = true; };
            textBox.TextChanged += (_, _) => { okBtn.IsEnabled = !string.IsNullOrWhiteSpace(textBox.Text); };
            okBtn.IsEnabled = false;

            // 打开后自动聚焦输入框
            dialog.Loaded += (_, _) => textBox.Focus();

            return dialog.ShowDialog() == true ? textBox.Text.Trim() : null;
        }
    }

    /// <summary>
    /// 子人格视图模型，支持 DataGrid 双向绑定
    /// </summary>
    public class SubPersonalityViewModel : INotifyPropertyChanged
    {
        private string _name = "";
        private string _description = "";
        private int _weight;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        public int Weight
        {
            get => _weight;
            set { _weight = value; OnPropertyChanged(nameof(Weight)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
