using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;

namespace MochiBot.Src.UI
{
    public partial class MainWindow : Window
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;
        private readonly UserConfigRepository? _userConfigRepository;
        private readonly List<string> _subscriptionIds = new();
        private ChatWindow? _chatWindow;
        private bool _webViewReady;
        private bool _isPassthrough;
        private PassthroughButtonWindow? _passthroughBtn;

        // Win32 鼠标穿透
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public MainWindow(IEventDispatcher eventDispatcher, IConfigReader configReader, UserConfigRepository? userConfigRepository = null)
        {
            _eventDispatcher = eventDispatcher;
            _configReader = configReader;
            _userConfigRepository = userConfigRepository;
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosing;

            // 窗口尺寸：桌面右下角，高度占 1/2
            var workArea = SystemParameters.WorkArea;
            Height = workArea.Height / 2.0;
            Width = Height * 0.7;
            Left = workArea.Right - Width;
            Top = workArea.Bottom - Height;
        }

        // ========== WebView2 初始化 ==========

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var resourcesPath = ResolveResourcesPath();

                _configReader.Logger.Info("[MainWindow] Initializing WebView2...");
                await webView.EnsureCoreWebView2Async(null);

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "vrm.local", resourcesPath, CoreWebView2HostResourceAccessKind.Allow);

                // 注册 .vrm 文件处理
                webView.CoreWebView2.AddWebResourceRequestedFilter("https://vrm.local/*.vrm", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.WebResourceRequested += (s, args) =>
                {
                    var localPath = args.Request.Uri.Replace("https://vrm.local/", resourcesPath + "\\");
                    localPath = Uri.UnescapeDataString(localPath);
                    if (File.Exists(localPath))
                    {
                        var bytes = File.ReadAllBytes(localPath);
                        var stream = new MemoryStream(bytes);
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            stream, 200, "OK", "Content-Type: application/octet-stream\r\n");
                    }
                };

                // 导航到 VRM Viewer
                var modelFileName = "QQ vrm 1.vrm";
                var encodedModelPath = Uri.EscapeDataString($"Data/{modelFileName}");
                var viewerUrl = $"https://vrm.local/Viewer/vrm-viewer.html?model={encodedModelPath}&t={DateTime.Now.Ticks}";
                webView.CoreWebView2.Navigate(viewerUrl);

                // 监听 JS ready 消息
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                _configReader.Logger.Info("[MainWindow] WebView2 navigation started");

                // 创建浮动穿透按钮（独立窗口，不受主窗口穿透影响）
                _passthroughBtn = new PassthroughButtonWindow(this);
                _passthroughBtn.Show();
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error($"[MainWindow] WebView2 init failed: {ex}");
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var json = args.WebMessageAsJson;
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString() == "ready")
                {
                    _webViewReady = true;
                    _configReader.Logger.Info("[MainWindow] VRM model ready, subscribing to events");
                    SubscribeToEvents();
                }
            }
            catch { }
        }

        // ========== 事件订阅 ==========

        private void SubscribeToEvents()
        {
            // 订阅心情变化事件
            var moodSubId = _eventDispatcher.Subscribe(EventCategory.MoodChange, OnMoodChange);
            _subscriptionIds.Add(moodSubId);

            // 订阅模块状态变更事件
            var stateSubId = _eventDispatcher.Subscribe(EventCategory.ModuleState, OnModuleStateChanged);
            _subscriptionIds.Add(stateSubId);
        }

        private void OnMoodChange(EventData eventData)
        {
            if (!_webViewReady) return;
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                var root = doc.RootElement;
                if (root.TryGetProperty("animation", out _)) return;
                if (root.TryGetProperty("mood", out var moodProp))
                {
                    var mood = moodProp.GetString()?.ToLower();
                    if (!string.IsNullOrEmpty(mood))
                        SendToViewer(new { type = "mood", expression = mood });
                }
            }
            catch { }
        }

        private void OnModuleStateChanged(EventData eventData)
        {
            if (!_webViewReady) return;
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                var root = doc.RootElement;
                var moduleId = root.TryGetProperty("moduleId", out var idProp) ? idProp.GetString() : null;
                if (moduleId != "agent") return;
                var state = root.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
                if (!string.IsNullOrEmpty(state))
                    SendToViewer(new { type = "state", state });
            }
            catch { }
        }

        private void SendToViewer(object message)
        {
            try
            {
                var json = JsonSerializer.Serialize(message);
                Dispatcher.Invoke(() =>
                {
                    if (webView.CoreWebView2 != null)
                        webView.CoreWebView2.PostWebMessageAsJson(json);
                });
            }
            catch { }
        }

        // ========== 窗口关闭 ==========

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var behavior = "Exit";
            try { behavior = _configReader.GetAppSettings().CloseBehavior; } catch { }

            if (behavior == "Hide")
            {
                e.Cancel = true;
                Hide();
                _configReader.Logger.Info("[MainWindow] 窗口已隐藏（CloseBehavior=Hide）");
            }
            else
            {
                // 取消事件订阅
                foreach (var subId in _subscriptionIds)
                    _eventDispatcher.Unsubscribe(subId);
                _subscriptionIds.Clear();

                _passthroughBtn?.ForceClose();
                _eventDispatcher.StopScheduler();
                Application.Current.Shutdown();
            }
        }

        // ========== UI 事件 ==========

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 右键切换按钮栏显隐
            buttonBar.Visibility = buttonBar.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>切换鼠标穿透状态（由浮动按钮调用）</summary>
        public void TogglePassthrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (_isPassthrough)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                _isPassthrough = false;
                Opacity = 1.0;
                _passthroughBtn?.SetState(false);
                _configReader.Logger.Info("[MainWindow] 鼠标穿透已关闭");
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                _isPassthrough = true;
                var cfgOpacity = _configReader.GetAppSettings().PassthroughOpacity;
                Opacity = cfgOpacity;
                _passthroughBtn?.SetState(true);
                _configReader.Logger.Info($"[MainWindow] 鼠标穿透已开启，透明度={cfgOpacity}");
            }
        }

        private void ChatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_chatWindow == null)
            {
                _chatWindow = new ChatWindow(_eventDispatcher, _configReader);
                _chatWindow.Owner = this;
            }

            if (_chatWindow.Visibility == Visibility.Visible)
                _chatWindow.Hide();
            else
            {
                _chatWindow.Show();
                _chatWindow.Activate();
            }
        }

        private void InteractButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 互动功能
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_configReader, _eventDispatcher, this, _userConfigRepository);
            settingsWindow.ShowDialog();
        }

        // ========== 工具方法 ==========

        private static string ResolveResourcesPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var resourcesPath = Path.Combine(baseDir, "Resources");
            if (Directory.Exists(resourcesPath))
                return resourcesPath;

            var rootDir = baseDir;
            for (int i = 0; i < 5; i++)
            {
                var parent = Directory.GetParent(rootDir);
                if (parent == null) break;
                rootDir = parent.FullName;
                if (File.Exists(Path.Combine(rootDir, "MochiBot.sln")))
                    return Path.Combine(rootDir, "Resources");
            }
            return resourcesPath;
        }
    }

    /// <summary>
    /// 浮动鼠标穿透按钮窗口
    /// 独立于主窗口，始终可交互、可拖拽
    /// </summary>
    public class PassthroughButtonWindow : Window
    {
        private readonly MainWindow _owner;
        private readonly Button _button;
        private bool _isDragging;
        private Point _dragStart;

        public PassthroughButtonWindow(MainWindow owner)
        {
            _owner = owner;

            // 窗口设置：无边框、透明、始终置顶
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Width = 44;
            Height = 44;

            // 初始位置：主窗口右下角
            Left = owner.Left + owner.Width - 56;
            Top = owner.Top + owner.Height - 56;

            // 按钮
            _button = new Button
            {
                Content = "🖱️",
                FontSize = 18,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new System.Windows.Thickness(0),
                Width = 44,
                Height = 44,
            };
            // 左键点击 → 切换穿透
            _button.Click += (_, _) => _owner.TogglePassthrough();

            // 右键拖拽 → 移动按钮位置
            _button.MouseRightButtonDown += (_, e) =>
            {
                _isDragging = true;
                _dragStart = e.GetPosition(null);
                _button.CaptureMouse();
                e.Handled = true;
            };
            _button.MouseRightButtonUp += (_, _) =>
            {
                _isDragging = false;
                _button.ReleaseMouseCapture();
            };
            _button.MouseMove += (_, e) =>
            {
                if (!_isDragging) return;
                var pos = e.GetPosition(null);
                Left += pos.X - _dragStart.X;
                Top += pos.Y - _dragStart.Y;
                _dragStart = pos;
            };

            Content = _button;

            // 跟随主窗口移动
            owner.LocationChanged += (_, _) => FollowOwner();
            owner.SizeChanged += (_, _) => FollowOwner();
        }

        private void FollowOwner()
        {
            Left = _owner.Left + _owner.Width - 56;
            Top = _owner.Top + _owner.Height - 56;
        }

        /// <summary>更新按钮外观（穿透状态）</summary>
        public void SetState(bool isPassthrough)
        {
            _button.Opacity = isPassthrough ? 0.4 : 1.0;
        }

        /// <summary>主窗口关闭时强制关闭此窗口</summary>
        public void ForceClose()
        {
            Closing -= null!;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 阻止用户关闭，只允许 ForceClose
            e.Cancel = true;
        }
    }
}
