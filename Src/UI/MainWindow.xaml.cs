using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MochiBot.Src.Core.Events;
using MochiBot.Src.Models;
using MochiBot.Src.Renderer;

namespace MochiBot.Src.UI
{
    public partial class MainWindow : Window
    {
        private CharacterRenderer _renderer = new();
        private System.Windows.Threading.DispatcherTimer _timer = new();
        private IEventDispatcher _eventDispatcher;
        private string? _replySubscriptionId;

        // 消息列表
        private ObservableCollection<ChatMessageItem> _messages = new();

        public MainWindow(IEventDispatcher eventDispatcher)
        {
            _eventDispatcher = eventDispatcher;
            InitializeComponent();
            Loaded += OnLoaded;
            messageList.ItemsSource = _messages;
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

                // 订阅回复事件
                SubscribeToReplyEvents();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 订阅 Agent 发布的回复事件
        /// </summary>
        private void SubscribeToReplyEvents()
        {
            try
            {
                if (_eventDispatcher != null)
                {
                    _replySubscriptionId = _eventDispatcher.Subscribe(EventCategory.ToolResult, (eventData) =>
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(eventData.Info);
                            if (doc.RootElement.TryGetProperty("type", out var typeProp))
                            {
                                var type = typeProp.GetString();
                                if (type == "reply")
                                {
                                    var content = doc.RootElement.TryGetProperty("content", out var contentProp)
                                        ? contentProp.GetString() ?? ""
                                        : "";
                                    if (!string.IsNullOrEmpty(content))
                                    {
                                        // 在 UI 线程上添加消息
                                        Dispatcher.Invoke(() =>
                                        {
                                            AddMessage("小琪", content);
                                        });
                                    }
                                }
                            }
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        /// <summary>
        /// 添加消息到气泡列表
        /// </summary>
        private void AddMessage(string sender, string text)
        {
            _messages.Add(new ChatMessageItem { Sender = sender, Text = text });

            // 自动滚动到底部
            if (messageList.Items.Count > 0)
            {
                messageList.ScrollIntoView(messageList.Items[messageList.Items.Count - 1]);
            }

            // 如果聊天框未打开，自动显示气泡提示
            if (chatBubble.Visibility != Visibility.Visible)
            {
                chatBubble.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 发送用户消息
        /// </summary>
        private void SendMessage()
        {
            var text = inputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            // 显示用户消息
            AddMessage("我", text);
            inputBox.Clear();

            // 发布用户输入事件，触发 Agent 处理
            if (_eventDispatcher != null)
            {
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.UserInput,
                    Trigger = MochiBot.Src.Models.EventTrigger.User,
                    Info = text
                });
            }
        }

        // ========== 事件处理 ==========

        private void OnFrameUpdated()
        {
            // DispatcherTimer 已在 UI 线程上运行，直接触发刷新
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
            // 允许拖动窗口
            DragMove();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        private void ToggleChatButton_Click(object sender, RoutedEventArgs e)
        {
            chatBubble.Visibility = chatBubble.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    /// <summary>
    /// 聊天消息项
    /// </summary>
    public class ChatMessageItem
    {
        public string Sender { get; set; } = "";
        public string Text { get; set; } = "";
    }
}
