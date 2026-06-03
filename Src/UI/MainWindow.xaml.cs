using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Renderer;
using MochiBot.Src.Services;
using static MochiBot.Src.Core.Constants;
using EventTrigger = MochiBot.Src.EventModels.EventTrigger;

namespace MochiBot.Src.UI
{
    public partial class MainWindow : Window
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;
        private readonly UserConfigRepository? _userConfigRepository;
        private ChatWindow? _chatWindow;
        private TrayIcon? _trayIcon;
        private string? _latestAgentMessage;

        // GIF 模式
        private System.Windows.Controls.Image? _gifImage;
        private CharacterRenderer? _renderer;
        private DispatcherTimer? _timer;
        private string? _moodSubscriptionId;

        // VRM 模式
        private WebView2? _webView;
        private readonly List<string> _subscriptionIds = new();
        private bool _webViewReady;

        // 共享
        private string _displayMode = "Gif";
        private string _avatarChar = "宠";
        private bool _isPassthrough;
        private Window? _passthroughWindow;

        /// <summary>当前是否处于穿透模式</summary>
        public bool IsPassthrough => _isPassthrough;

        // Win32 鼠标穿透
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const uint SWP_NOZORDER = 0x0004;

        public MainWindow(IEventDispatcher eventDispatcher, IConfigReader configReader, UserConfigRepository? userConfigRepository = null)
        {
            _eventDispatcher = eventDispatcher;
            _configReader = configReader;
            _userConfigRepository = userConfigRepository;
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += OnClosing;

            // 窗口尺寸：桌面高度 1/3，按角色比例
            var workArea = SystemParameters.WorkArea;
            Height = workArea.Height / 3.0;
            Width = Height * (512.0 / 689.0);
            Left = workArea.Right - Width;
            Top = workArea.Bottom - Height;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var personality = _configReader.GetActivePersonality();
                var characterName = personality?.Name ?? "宠";
                _avatarChar = characterName.Length > 0 ? characterName[..1] : "宠";

                // 创建聊天窗口
                _chatWindow = new ChatWindow(_eventDispatcher, _configReader);
                _chatWindow.NewAgentMessage += OnChatWindowNewMessage;
                _chatWindow.Owner = this;

                // 根据配置初始化显示模式
                _displayMode = personality?.DisplayMode ?? "Gif";
                if (_displayMode == "Vrm")
                    await InitializeVrmMode();
                else
                    InitializeGifMode();

                _configReader.Logger.Info($"[MainWindow] 已初始化，显示模式={_displayMode}");
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error($"[MainWindow] 初始化失败: {ex.Message}");
            }
        }

        // ========== GIF 模式 ==========

        private async void InitializeGifMode()
        {
            _gifImage = new System.Windows.Controls.Image
            {
                Stretch = System.Windows.Media.Stretch.Uniform
            };
            characterContainer.Children.Add(_gifImage);

            var imagesPath = ResolveImagesPath();
            _renderer = new CharacterRenderer();
            await _renderer.InitializeAsync(imagesPath);
            _renderer.FrameUpdated += OnFrameUpdated;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (_, _) => UpdateImage();
            _timer.Start();

            SubscribeToGifMoodEvents();
        }

        private string ResolveImagesPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var imagesPath = Path.Combine(baseDir, "Resources", "Images");
            if (!Directory.Exists(imagesPath))
            {
                var rootDir = baseDir;
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
            return imagesPath;
        }

        private void SubscribeToGifMoodEvents()
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
                            Dispatcher.Invoke(() => _renderer?.PlayAnimation(animationName));
                            return;
                        }
                    }

                    if (root.TryGetProperty("mood", out var moodProp))
                    {
                        var moodStr = moodProp.GetString();
                        if (Enum.TryParse<AgentMood>(moodStr, true, out var mood))
                        {
                            Dispatcher.Invoke(() => _renderer?.SetMotion(mood));
                        }
                    }
                }
                catch { }
            });
        }

        private void OnFrameUpdated()
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateImage()));
        }

        private void UpdateImage()
        {
            if (_gifImage == null || _renderer == null) return;
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
                _gifImage.Source = bitmap;
            }
        }

        // ========== VRM 模式 ==========

        private async Task InitializeVrmMode()
        {
            _webView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            characterContainer.Children.Add(_webView);

            var resourcesPath = ResolveResourcesPath();

            _configReader.Logger.Info("[MainWindow] Initializing WebView2...");
            var envOptions = new CoreWebView2EnvironmentOptions(
                additionalBrowserArguments: "--enable-features=msWebView2EnableDraggableRegions");
            var env = await CoreWebView2Environment.CreateAsync(null, null, envOptions);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            // 虚拟主机映射
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "vrm.local", resourcesPath, CoreWebView2HostResourceAccessKind.Allow);

            // 注册 .vrm 文件处理
            _webView.CoreWebView2.AddWebResourceRequestedFilter(
                "https://vrm.local/*.vrm", CoreWebView2WebResourceContext.All);
            _webView.CoreWebView2.WebResourceRequested += (s, args) =>
            {
                var localPath = args.Request.Uri.Replace("https://vrm.local/", resourcesPath + "\\");
                localPath = Uri.UnescapeDataString(localPath);
                if (File.Exists(localPath))
                {
                    var bytes = File.ReadAllBytes(localPath);
                    var stream = new MemoryStream(bytes);
                    args.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
                        stream, 200, "OK", "Content-Type: application/octet-stream\r\n");
                }
            };

            // 导航到 VRM Viewer
            var modelFileName = "QQ vrm 1.vrm";
            var encodedModelPath = Uri.EscapeDataString($"Data/{modelFileName}");
            var viewerUrl = $"https://vrm.local/Viewer/vrm-viewer.html?model={encodedModelPath}&t={DateTime.Now.Ticks}";
            _webView.CoreWebView2.Navigate(viewerUrl);

            // 监听 JS 消息
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // 手动设置 WebView2 渲染区域（避免 HWND 空间问题覆盖工具栏）
            UpdateWebViewBounds();
            characterContainer.SizeChanged += (_, _) => UpdateWebViewBounds();

            _configReader.Logger.Info("[MainWindow] WebView2 initialized");
        }

        /// <summary>将 WebView2 HWND 限制为 characterContainer 的实际大小（物理像素）</summary>
        private void UpdateWebViewBounds()
        {
            if (_webView?.CoreWebView2 == null) return;

            Dispatcher.BeginInvoke(() =>
            {
                var w = characterContainer.ActualWidth;
                var h = characterContainer.ActualHeight;
                if (w <= 0 || h <= 0) return;

                // WPF 设备无关单位 → 物理像素
                var source = PresentationSource.FromVisual(this);
                var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                var hwnd = new WindowInteropHelper(this).Handle;
                // WebView2 的 HWND 是窗口的子窗口，直接调整其大小
                var wv2Hwnd = FindWindowEx(hwnd, IntPtr.Zero, "Chrome_WidgetWin_0", null);
                if (wv2Hwnd != IntPtr.Zero)
                {
                    SetWindowPos(wv2Hwnd, HWND_TOP, 0, 0,
                        (int)(w * dpiX), (int)(h * dpiY), SWP_NOZORDER);
                }
            }, DispatcherPriority.Loaded);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter,
            string? className, string? windowTitle);

        private string ResolveResourcesPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var resourcesPath = Path.Combine(baseDir, "Resources");
            if (!Directory.Exists(resourcesPath))
            {
                var rootDir = baseDir;
                for (int i = 0; i < 5; i++)
                {
                    var parent = Directory.GetParent(rootDir);
                    if (parent == null) break;
                    rootDir = parent.FullName;
                    if (File.Exists(Path.Combine(rootDir, "MochiBot.sln")))
                        break;
                }
                resourcesPath = Path.Combine(rootDir, "Resources");
            }
            return resourcesPath;
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var json = args.WebMessageAsJson;
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp)) return;

                switch (typeProp.GetString())
                {
                    case "ready":
                        _webViewReady = true;
                        _configReader.Logger.Info("[MainWindow] VRM model ready, subscribing to events");
                        SubscribeToVrmEvents();
                        break;
                    case "toggleToolbar":
                        Dispatcher.Invoke(ToggleToolbar);
                        break;
                }
            }
            catch { }
        }

        private void SubscribeToVrmEvents()
        {
            var moodSubId = _eventDispatcher.Subscribe(EventCategory.MoodChange, OnMoodChange);
            _subscriptionIds.Add(moodSubId);

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
                    if (_webView?.CoreWebView2 != null)
                        _webView.CoreWebView2.PostWebMessageAsJson(json);
                });
            }
            catch { }
        }

        // ========== 气泡消息（统一 WPF 覆盖层） ==========

        public void ShowBubble(string text, string avatarChar)
        {
            Dispatcher.Invoke(() =>
            {
                bubbleAvatarText.Text = string.IsNullOrEmpty(avatarChar) ? _avatarChar : avatarChar;
                var displayText = text.Length > 50 ? text[..50] + "..." : text;
                bubbleText.Text = displayText;
                chatBubble.Visibility = Visibility.Visible;
            });
        }

        public void HideBubble()
        {
            Dispatcher.Invoke(() => chatBubble.Visibility = Visibility.Collapsed);
        }

        private void Bubble_Click(object sender, MouseButtonEventArgs e)
        {
            chatBubble.Visibility = Visibility.Collapsed;
            ToggleChatButton_Click(this, new RoutedEventArgs());
        }

        private void OnChatWindowNewMessage()
        {
            Dispatcher.Invoke(() =>
            {
                if (_chatWindow == null) return;

                _latestAgentMessage = _chatWindow.LatestAgentMessage;

                // 如果聊天窗口未打开，显示气泡
                if (_chatWindow.Visibility != Visibility.Visible)
                {
                    var personality = _configReader.GetActivePersonality();
                    var avatarChar = personality?.Name;
                    avatarChar = !string.IsNullOrEmpty(avatarChar) ? avatarChar[..1] : "宠";
                    ShowBubble(_latestAgentMessage ?? "", avatarChar);
                }
            });
        }

        // ========== 鼠标穿透 ==========

        /// <summary>设置穿透模式（供设置窗口调用）</summary>
        public void SetPassthrough(bool enable)
        {
            if (enable == _isPassthrough) return;
            TogglePassthrough();
        }

        public void TogglePassthrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (_isPassthrough)
            {
                // 关闭穿透：移除 WS_EX_TRANSPARENT，恢复不透明
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                _isPassthrough = false;
                SetWindowOpacity(1.0);
                ClosePassthroughWindow();
                _configReader.Logger.Info("[MainWindow] 鼠标穿透已关闭");
            }
            else
            {
                // 开启穿透：添加 WS_EX_TRANSPARENT（WS_EX_LAYERED 始终保留）
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                _isPassthrough = true;
                var cfgOpacity = _configReader.GetAppSettings().PassthroughOpacity;
                SetWindowOpacity(cfgOpacity);
                ShowPassthroughWindow();
                _configReader.Logger.Info($"[MainWindow] 鼠标穿透已开启，透明度={cfgOpacity}");
            }
        }

        /// <summary>设置窗口透明度，0.0~1.0</summary>
        public void SetWindowOpacity(double opacity)
        {
            opacity = Math.Clamp(opacity, 0.0, 1.0);
            if (_displayMode == "Vrm" && _webView?.CoreWebView2 != null)
            {
                // VRM 模式：JS 设置 WebView2 内容透明度 + WPF 按键栏透明度
                SendToViewer(new { type = "opacity", value = opacity });
                toolbarPanel.Opacity = opacity;
            }
            else
            {
                // GIF 模式：使用 WPF 原生透明度（整体生效，含按键栏）
                this.Opacity = opacity;
            }
        }

        /// <summary>创建穿透模式控制窗口（独立窗口，不被穿透）</summary>
        private void ShowPassthroughWindow()
        {
            if (_passthroughWindow != null) return;

            var btn = new System.Windows.Controls.Button
            {
                Content = "📌",
                FontSize = 14,
                Width = 36,
                Height = 36,
                Background = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Click += (_, _) => TogglePassthrough();

            _passthroughWindow = new Window
            {
                Content = btn,
                Width = 42,
                Height = 42,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                Owner = this
            };

            PositionPassthroughWindow();
            _passthroughWindow.Show();

            // 跟随主窗口移动
            LocationChanged += (_, _) => PositionPassthroughWindow();
        }

        private void PositionPassthroughWindow()
        {
            if (_passthroughWindow == null) return;
            // 放在主窗口右下角
            _passthroughWindow.Left = Left + Width - _passthroughWindow.Width - 4;
            _passthroughWindow.Top = Top + Height + 4;
        }

        private void ClosePassthroughWindow()
        {
            if (_passthroughWindow == null) return;
            _passthroughWindow.Close();
            _passthroughWindow = null;
        }

        // ========== 系统托盘 ==========

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _trayIcon = new TrayIcon(hwnd, "MochiBot", RestoreFromTray);
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProcHook);
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_trayIcon?.ProcessMessage(msg, wParam, lParam) == true)
            {
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            _trayIcon?.Show();
            _configReader.Logger.Info("[MainWindow] 已最小化到系统托盘");
        }

        private void RestoreFromTray()
        {
            Show();
            Activate();
            _trayIcon?.Hide();
            _configReader.Logger.Info("[MainWindow] 已从系统托盘恢复");
        }

        // ========== UI 事件 ==========

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ToggleToolbar();
        }

        private void ToggleToolbar()
        {
            toolbarPanel.Visibility = toolbarPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
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
                HideBubble();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_configReader, _eventDispatcher, this, _userConfigRepository);
            settingsWindow.ShowDialog();
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
                // 穿透窗口清理
                ClosePassthroughWindow();

                // GIF 模式清理
                _timer?.Stop();
                if (!string.IsNullOrEmpty(_moodSubscriptionId))
                    _eventDispatcher.Unsubscribe(_moodSubscriptionId);

                // VRM 模式清理
                foreach (var subId in _subscriptionIds)
                    _eventDispatcher.Unsubscribe(subId);
                _subscriptionIds.Clear();
                _webView?.Dispose();

                _trayIcon?.Dispose();
                _eventDispatcher.StopScheduler();
                Application.Current.Shutdown();
            }
        }
    }
}
