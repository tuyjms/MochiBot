using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Renderer;
using MochiBot.Src.Services;
using EventTrigger = MochiBot.Src.EventModels.EventTrigger;

namespace MochiBot.Src.UI
{
    public partial class MainWindow : Window
    {
        private CharacterRenderer _renderer = new();
        private System.Windows.Threading.DispatcherTimer _timer = new();
        private IEventDispatcher _eventDispatcher;
        private IConfigReader _configReader;
        private UserConfigRepository? _userConfigRepository;
        private string? _moodSubscriptionId;
        private ChatWindow? _chatWindow;

        // 最新一条 agent 消息（用于气泡显示）
        private string? _latestAgentMessage;

        public MainWindow(IEventDispatcher eventDispatcher, IConfigReader configReader, UserConfigRepository? userConfigRepository = null)
        {
            _eventDispatcher = eventDispatcher;
            _configReader = configReader;
            _userConfigRepository = userConfigRepository;
            InitializeComponent();
            Loaded += OnLoaded;

            // 设置窗口位置：桌面右下角，高度占1/3，宽度按角色图片比例 512:689 缩放
            var workArea = SystemParameters.WorkArea;
            Height = workArea.Height / 3.0;
            Width = Height * (512.0 / 689.0); // 角色图片宽高比 512:689
            Left = workArea.Right - Width;
            Top = workArea.Bottom - Height;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 查找资源路径
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var imagesPath = Path.Combine(baseDir, "Resources", "Images");
                if (!Directory.Exists(imagesPath))
                {
                    var rootDir = AppDomain.CurrentDomain.BaseDirectory;
                    for (int i = 0; i < 5; i++)
                    {
                        var parent = Directory.GetParent(rootDir);
                        if (parent == null) break;
                        rootDir = parent.FullName;
                        if (File.Exists(Path.Combine(rootDir, "MochiBot.sln")))
                            break;
                    }
                    imagesPath = Path.Combine(rootDir, "Resources", "Images");
                }

                await _renderer.InitializeAsync(imagesPath);
                _renderer.FrameUpdated += OnFrameUpdated;

                // 启动定时器刷新
                _timer.Interval = TimeSpan.FromMilliseconds(50);
                _timer.Tick += (s, args) => UpdateImage();
                _timer.Start();

                // 订阅情绪事件
                SubscribeToMoodEvents();

                // 创建聊天窗口（延迟创建，避免影响启动速度）
                CreateChatWindow();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建聊天窗口实例
        /// </summary>
        private void CreateChatWindow()
        {
            _chatWindow = new ChatWindow(_eventDispatcher, _configReader);
            _chatWindow.NewAgentMessage += OnChatWindowNewMessage;

            // 设置 Owner 使聊天窗口在主窗口之上
            _chatWindow.Owner = this;

            // 从配置读取角色名称更新气泡头像
            var personality = _configReader.GetActivePersonality();
            var characterName = personality?.Name ?? "小琪";
            bubbleAvatarText.Text = characterName.Length > 0 ? characterName[..1] : "琪";
        }

        /// <summary>
        /// 聊天窗口收到新消息时更新主窗口气泡
        /// </summary>
        private void OnChatWindowNewMessage()
        {
            Dispatcher.Invoke(() =>
            {
                if (_chatWindow != null)
                {
                    _latestAgentMessage = _chatWindow.LatestAgentMessage;
                    UpdateBubbleText();

                    // 如果聊天窗口未打开，显示气泡
                    if (_chatWindow.Visibility != Visibility.Visible)
                    {
                        chatBubble.Visibility = Visibility.Visible;
                    }
                }
            });
        }

        /// <summary>
        /// 更新气泡文本
        /// </summary>
        private void UpdateBubbleText()
        {
            if (!string.IsNullOrEmpty(_latestAgentMessage))
            {
                // 截取前 50 个字符作为气泡预览
                var preview = _latestAgentMessage.Length > 50
                    ? _latestAgentMessage[..50] + "..."
                    : _latestAgentMessage;
                bubbleText.Text = preview;
            }
        }

        /// <summary>
        /// 订阅情绪变化事件
        /// </summary>
        private void SubscribeToMoodEvents()
        {
            try
            {
                if (_eventDispatcher != null)
                {
                    _moodSubscriptionId = _eventDispatcher.Subscribe(EventCategory.MoodChange, (eventData) =>
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(eventData.Info);
                            var root = doc.RootElement;

                            if (root.TryGetProperty("animation", out var animProp))
                            {
                                var animationName = animProp.GetString();
                                if (!string.IsNullOrEmpty(animationName))
                                {
                                    Dispatcher.Invoke(() => _renderer.PlayAnimation(animationName));
                                    return;
                                }
                            }

                            if (root.TryGetProperty("mood", out var moodProp))
                            {
                                var moodStr = moodProp.GetString();
                                if (Enum.TryParse<AgentMood>(moodStr, true, out var mood))
                                {
                                    Dispatcher.Invoke(() => _renderer.SetMotion(mood));
                                }
                            }
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        // ========== 事件处理 ==========

        private void OnFrameUpdated()
        {
            UpdateImage();
        }

        private void UpdateImage()
        {
            var frameBytes = _renderer.CurrentFrame;
            if (frameBytes != null)
            {
                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(frameBytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                characterImage.Source = bitmap;
            }
        }

        // ========== UI 事件 ==========

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void ToggleChatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_chatWindow == null) return;

            if (_chatWindow.Visibility == Visibility.Visible)
            {
                _chatWindow.Hide();
            }
            else
            {
                _chatWindow.Show();
                _chatWindow.Activate();
                // 隐藏主窗口气泡
                chatBubble.Visibility = Visibility.Collapsed;
            }
        }

        private void Bubble_Click(object sender, RoutedEventArgs e)
        {
            // 点击气泡打开聊天窗口
            ToggleChatButton_Click(sender, e);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_configReader, _eventDispatcher, this, _userConfigRepository);
            settingsWindow.ShowDialog();
        }
    }
}
