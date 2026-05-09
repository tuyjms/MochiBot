using System.IO;
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
            // 验证输入
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
                // 1. 保存 AppSettings（修改 JSON 文件）
                var appSettings = _configReader.GetAppSettings();
                appSettings.UserName = userNameBox.Text.Trim();
                await SaveAppSettingsAsync(appSettings);

                // 2. 保存数据库配置（透明度、碎碎念等）
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

                // 3. 立即应用透明度
                _ownerWindow.Opacity = opacitySlider.Value;

                // 4. 发布配置变更事件（触发热重载）
                var changedItems = new List<string>();
                if (appSettings.UserName != userNameBox.Text.Trim())
                    changedItems.Add("UserName");

                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.ConfigChanged,
                    Trigger = EventTrigger.User,
                    Info = JsonSerializer.Serialize(new
                    {
                        changedItems
                    })
                });

                _configReader.Logger.Info("[Settings] 配置已保存并应用热重载");

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error($"[Settings] 保存配置失败", ex);
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>保存 AppSettings 到 JSON 文件</summary>
        private async Task SaveAppSettingsAsync(AppSettings newSettings)
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "appsettings.json");

            // 读取现有 JSON
            var json = await File.ReadAllTextAsync(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 构建新的 JSON
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();

            // 复制 Providers
            if (root.TryGetProperty("Providers", out var providers))
            {
                writer.WritePropertyName("Providers");
                writer.WriteRawValue(providers.GetRawText());
            }

            // 写入更新后的 AppSettings
            writer.WritePropertyName("AppSettings");
            writer.WriteStartObject();
            writer.WriteString("UserName", newSettings.UserName);
            writer.WriteString("ActivePersonality", newSettings.ActivePersonality);
            writer.WriteBoolean("EnableStructuredResponse", newSettings.EnableStructuredResponse);
            writer.WriteNumber("MaxActionsPerResponse", newSettings.MaxActionsPerResponse);
            writer.WriteBoolean("EnableMidTermMemoryOnChat", newSettings.EnableMidTermMemoryOnChat);
            writer.WriteBoolean("EnableLongTermRecall", newSettings.EnableLongTermRecall);
            writer.WriteString("LogLevel", newSettings.LogLevel);
            writer.WriteBoolean("LogToFile", newSettings.LogToFile);
            writer.WriteBoolean("LogToConsole", newSettings.LogToConsole);
            writer.WriteEndObject();

            // 复制 ModuleSettings
            if (root.TryGetProperty("ModuleSettings", out var moduleSettings))
            {
                writer.WritePropertyName("ModuleSettings");
                writer.WriteRawValue(moduleSettings.GetRawText());
            }

            // 复制 CronTasks
            if (root.TryGetProperty("CronTasks", out var cronTasks))
            {
                writer.WritePropertyName("CronTasks");
                writer.WriteRawValue(cronTasks.GetRawText());
            }

            writer.WriteEndObject();
            writer.Flush();

            // 写回文件
            stream.Position = 0;
            var newJson = new StreamReader(stream).ReadToEnd();
            await File.WriteAllTextAsync(configPath, newJson);

            // 重新加载配置
            _configReader.Reload();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
