using System.Text.Json;
using System.Windows;
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

            LoadCurrentSettings();
        }

        /// <summary>加载当前配置到 UI 控件</summary>
        private void LoadCurrentSettings()
        {
            var appSettings = _configReader.GetAppSettings();
            userNameBox.Text = appSettings.UserName;

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
                    // 数据库加载失败时使用默认值
                    opacitySlider.Value = 1.0;
                    murmurEnabledCheck.IsChecked = true;
                    murmurIntervalBox.Text = "30";
                }
            }
            else
            {
                opacitySlider.Value = 1.0;
                murmurEnabledCheck.IsChecked = true;
                murmurIntervalBox.Text = "30";
            }

            UpdateOpacityValue();
        }

        /// <summary>更新透明度显示值</summary>
        private void UpdateOpacityValue()
        {
            opacityValue.Text = $"{opacitySlider.Value:F1}";
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateOpacityValue();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
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

            try
            {
                var oldUserName = _configReader.GetAppSettings().UserName;
                var newUserName = userNameBox.Text.Trim();

                var appSettings = _configReader.GetAppSettings();
                appSettings.UserName = newUserName;
                _configReader.SaveAppSettings(appSettings);

                if (_userConfigRepository != null)
                {
                    var userConfig = new UserConfig
                    {
                        Name = "小可爱",
                        Personality = "温柔",
                        Opacity = opacitySlider.Value,
                        MurmurEnabled = murmurEnabledCheck.IsChecked ?? true,
                        MurmurInterval = murmurInterval,
                        WindowPosX = (int)_ownerWindow.Left,
                        WindowPosY = (int)_ownerWindow.Top
                    };
                    await _userConfigRepository.SaveConfigAsync(userConfig);
                }

                _ownerWindow.Opacity = opacitySlider.Value;

                var changedItems = new List<string>();
                if (oldUserName != newUserName) changedItems.Add("UserName");

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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
