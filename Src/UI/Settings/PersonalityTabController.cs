using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;

namespace MochiBot.Src.UI.Settings
{
    /// <summary>
    /// Tab 3: 人格编辑 — 人格 CRUD、聊天模型管理、子人格管理
    /// </summary>
    public class PersonalityTabController
    {
        private readonly IConfigReader _configReader;

        // UI 控件引用
        private readonly ComboBox _personalitySelector;
        private readonly TextBox _personNameBox;
        private readonly TextBox _personDescBox;
        private readonly ComboBox _displayModeBox;
        private readonly ComboBox _modelProviderBox;
        private readonly ComboBox _modelNameBox;
        private readonly ListBox _chatModelsList;
        private readonly ComboBox _visionProviderBox;
        private readonly ComboBox _visionModelBox;
        private readonly ListBox _visionModelsList;
        private readonly DataGrid _subPersonalitiesGrid;
        private readonly TextBlock _weightSumLabel;
        private readonly ComboBox _activePersonalityBox;

        // 数据源
        private ObservableCollection<string> _chatModels = new();
        private ObservableCollection<string> _visionModels = new();
        private ObservableCollection<SubPersonalityViewModel> _subPersonalities = new();
        private Dictionary<string, PersonalityConfig> _personalityCache = new();
        private string? _currentPersonalityName;

        public PersonalityTabController(
            IConfigReader configReader,
            ComboBox personalitySelector,
            TextBox personNameBox,
            TextBox personDescBox,
            ComboBox displayModeBox,
            ComboBox modelProviderBox,
            ComboBox modelNameBox,
            ListBox chatModelsList,
            ComboBox visionProviderBox,
            ComboBox visionModelBox,
            ListBox visionModelsList,
            DataGrid subPersonalitiesGrid,
            TextBlock weightSumLabel,
            ComboBox activePersonalityBox)
        {
            _configReader = configReader;
            _personalitySelector = personalitySelector;
            _personNameBox = personNameBox;
            _personDescBox = personDescBox;
            _displayModeBox = displayModeBox;
            _modelProviderBox = modelProviderBox;
            _modelNameBox = modelNameBox;
            _chatModelsList = chatModelsList;
            _visionProviderBox = visionProviderBox;
            _visionModelBox = visionModelBox;
            _visionModelsList = visionModelsList;
            _subPersonalitiesGrid = subPersonalitiesGrid;
            _weightSumLabel = weightSumLabel;
            _activePersonalityBox = activePersonalityBox;

            _subPersonalities.CollectionChanged += (_, _) => UpdateWeightSum();
        }

        /// <summary>加载人格列表和提供商下拉</summary>
        public void Load(string? selectedName)
        {
            var personalities = _configReader.GetAvailablePersonalities();
            _personalitySelector.ItemsSource = personalities;

            _modelProviderBox.ItemsSource = _configReader.GetAvailableProviders().ToList();
            if (_modelProviderBox.Items.Count > 0)
                _modelProviderBox.SelectedIndex = 0;

            _visionProviderBox.ItemsSource = _configReader.GetAvailableProviders().ToList();
            if (_visionProviderBox.Items.Count > 0)
                _visionProviderBox.SelectedIndex = 0;

            _personalityCache.Clear();
            foreach (var name in personalities)
            {
                var config = _configReader.LoadPersonality(name);
                if (config != null)
                    _personalityCache[name] = config;
            }

            if (!string.IsNullOrEmpty(selectedName) && personalities.Contains(selectedName))
                _personalitySelector.SelectedItem = selectedName;
            else if (personalities.Count > 0)
                _personalitySelector.SelectedIndex = 0;
        }

        /// <summary>切换人格时加载详情</summary>
        public void OnPersonalityChanged()
        {
            if (_personalitySelector.SelectedItem is not string name) return;
            _currentPersonalityName = name;

            if (_personalityCache.TryGetValue(name, out var config))
            {
                _personNameBox.Text = config.Name;
                _personDescBox.Text = config.Description;

                var displayMode = config.DisplayMode ?? "Gif";
                _displayModeBox.SelectedIndex = displayMode == "Vrm" ? 1 : 0;

                _chatModels = new ObservableCollection<string>(config.ChatModels ?? new List<string>());
                _chatModelsList.ItemsSource = _chatModels;

                _visionModels = new ObservableCollection<string>(config.VisionModels ?? new List<string>());
                _visionModelsList.ItemsSource = _visionModels;

                _subPersonalities = new ObservableCollection<SubPersonalityViewModel>(
                    (config.Personalities ?? new List<SubPersonality>()).Select(s =>
                        new SubPersonalityViewModel { Name = s.Name, Description = s.Description, Weight = s.Weight }));
                _subPersonalities.CollectionChanged += (_, _) => UpdateWeightSum();
                _subPersonalitiesGrid.ItemsSource = _subPersonalities;

                UpdateWeightSum();
            }
        }

        /// <summary>切换提供商时，从模型注册表加载模型列表填充下拉框</summary>
        public void OnProviderChanged()
        {
            var provider = _modelProviderBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(provider)) return;

            var providerConfig = _configReader.GetProvider(provider);
            var models = providerConfig?.Models?
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => m.Name)
                .ToList() ?? new List<string>();

            _modelNameBox.ItemsSource = models;
            if (models.Count > 0)
                _modelNameBox.SelectedIndex = 0;
            else
                _modelNameBox.ItemsSource = null;
        }

        public void AddChatModel()
        {
            var provider = _modelProviderBox.SelectedItem?.ToString()?.Trim();
            var model = _modelNameBox.SelectedItem?.ToString()?.Trim();
            if (string.IsNullOrEmpty(provider))
            {
                MessageBox.Show("请先选择一个提供商", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(model))
            {
                MessageBox.Show("请先选择一个模型", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var fullName = $"{provider}/{model}";
            if (_chatModels.Contains(fullName))
            {
                MessageBox.Show($"模型 \"{fullName}\" 已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _chatModels.Add(fullName);
        }

        public void RemoveChatModel()
        {
            if (_chatModelsList.SelectedItem is string selected)
                _chatModels.Remove(selected);
        }

        /// <summary>视觉模型提供商切换时，加载模型列表</summary>
        public void OnVisionProviderChanged()
        {
            var provider = _visionProviderBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(provider)) return;

            var providerConfig = _configReader.GetProvider(provider);
            var models = providerConfig?.Models?
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => m.Name)
                .ToList() ?? new List<string>();

            _visionModelBox.ItemsSource = models;
            if (models.Count > 0)
                _visionModelBox.SelectedIndex = 0;
            else
                _visionModelBox.ItemsSource = null;
        }

        public void AddVisionModel()
        {
            var provider = _visionProviderBox.SelectedItem?.ToString()?.Trim();
            var model = _visionModelBox.SelectedItem?.ToString()?.Trim();
            if (string.IsNullOrEmpty(provider))
            {
                MessageBox.Show("请先选择一个提供商", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(model))
            {
                MessageBox.Show("请先选择一个模型", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var fullName = $"{provider}/{model}";
            if (_visionModels.Contains(fullName))
            {
                MessageBox.Show($"模型 \"{fullName}\" 已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _visionModels.Add(fullName);
        }

        public void RemoveVisionModel()
        {
            if (_visionModelsList.SelectedItem is string selected)
                _visionModels.Remove(selected);
        }

        public void AddSubPersonality()
        {
            _subPersonalities.Add(new SubPersonalityViewModel { Name = string.Empty, Description = string.Empty, Weight = 0 });
        }

        public void RemoveSubPersonality()
        {
            if (_subPersonalitiesGrid.SelectedItem is SubPersonalityViewModel selected)
                _subPersonalities.Remove(selected);
        }

        public void AddPersonality()
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

                RefreshSelector();
                _personalitySelector.SelectedItem = name;
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error("[Settings] 新增人格失败", ex);
                MessageBox.Show($"新增人格失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void RemovePersonality()
        {
            if (_personalitySelector.SelectedItem is not string name) return;

            var activeName = _activePersonalityBox.SelectedItem?.ToString();
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

                RefreshSelector();
                if (_personalitySelector.Items.Count > 0)
                    _personalitySelector.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error("[Settings] 删除人格失败", ex);
                MessageBox.Show($"删除人格失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>验证子人格权重</summary>
        public bool ValidateWeightSum()
        {
            if (_subPersonalities.Count <= 0) return true;
            var weightSum = _subPersonalities.Sum(s => s.Weight);
            if (weightSum != PersonalityConfig.SubPersonalityWeightSum)
            {
                MessageBox.Show($"子人格权重总和为 {weightSum}，应为 {PersonalityConfig.SubPersonalityWeightSum}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        /// <summary>保存当前人格配置</summary>
        public void SaveCurrent()
        {
            if (string.IsNullOrEmpty(_currentPersonalityName)) return;
            if (!_personalityCache.TryGetValue(_currentPersonalityName, out var config)) return;

            config.Name = _personNameBox.Text.Trim();
            config.Description = _personDescBox.Text;
            config.DisplayMode = (_displayModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Gif";
            config.ChatModels = _chatModels.ToList();
            config.VisionModels = _visionModels.Count > 0 ? _visionModels.ToList() : null;
            config.Personalities = _subPersonalities.Select(s => new SubPersonality
            {
                Name = s.Name,
                Description = s.Description,
                Weight = s.Weight
            }).ToList();

            _configReader.SavePersonality(_currentPersonalityName, config);
        }

        private void RefreshSelector()
        {
            _personalitySelector.ItemsSource = _configReader.GetAvailablePersonalities();
        }

        private void UpdateWeightSum()
        {
            var required = PersonalityConfig.SubPersonalityWeightSum;
            var sum = _subPersonalities.Sum(s => s.Weight);
            _weightSumLabel.Text = $"权重总和: {sum}" + (sum != required ? $"（应为 {required}）" : "");
            _weightSumLabel.Foreground = sum == required
                ? System.Windows.Media.Brushes.Gray
                : System.Windows.Media.Brushes.Red;
        }

        private string? ShowInputDialog(string prompt, string title)
        {
            var dialog = new Window
            {
                Title = title, Width = 360, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(_personalitySelector),
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
            dialog.Loaded += (_, _) => textBox.Focus();

            return dialog.ShowDialog() == true ? textBox.Text.Trim() : null;
        }
    }
}
