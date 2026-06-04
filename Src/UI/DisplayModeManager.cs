using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MochiBot.Src.Core;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Renderer;
using static MochiBot.Src.UI.PassthroughManager;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 显示模式管理器
    /// 负责 GIF（CharacterRenderer）和 VRM（WebView2）两种显示模式的初始化、事件订阅和生命周期管理
    /// </summary>
    public class DisplayModeManager : IDisposable
    {
        private readonly Panel _container;
        private readonly IConfigReader _configReader;
        private readonly IEventDispatcher _eventDispatcher;

        // GIF 模式
        private Image? _gifImage;
        private CharacterRenderer? _renderer;
        private DispatcherTimer? _timer;
        private string? _moodSubscriptionId;

        // VRM 模式
        private WebView2? _webView;
        private readonly List<string> _subscriptionIds = new();
        private bool _webViewReady;

        // 共享
        private string _displayMode = "Gif";

        /// <summary>当前显示模式（"Gif"/"Vrm"）</summary>
        public string DisplayMode => _displayMode;

        /// <summary>VRM WebView2 是否就绪</summary>
        public bool IsWebViewReady => _webViewReady;

        public DisplayModeManager(
            Panel container,
            IConfigReader configReader,
            IEventDispatcher eventDispatcher)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        }

        /// <summary>
        /// 根据人格配置初始化对应的显示模式
        /// </summary>
        public async Task InitializeAsync(PersonalityConfig? personality)
        {
            _displayMode = personality?.DisplayMode ?? "Gif";

            if (_displayMode == "Vrm")
                await InitializeVrmMode();
            else
                await InitializeGifMode();

            _configReader.Logger.Info($"[DisplayMode] 已初始化，显示模式={_displayMode}");
        }

        // ========== GIF 模式 ==========

        private async Task InitializeGifMode()
        {
            _gifImage = new Image
            {
                Stretch = System.Windows.Media.Stretch.Uniform
            };
            _container.Children.Add(_gifImage);

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
            return Path.Combine(AppPaths.ResourcesDir, "Images");
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
                            System.Windows.Application.Current.Dispatcher.Invoke(() => _renderer?.PlayAnimation(animationName));
                            return;
                        }
                    }

                    if (root.TryGetProperty("mood", out var moodProp))
                    {
                        var moodStr = moodProp.GetString();
                        if (Enum.TryParse<AgentMood>(moodStr, true, out var mood))
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() => _renderer?.SetMotion(mood));
                        }
                    }
                }
                catch { }
            });
        }

        private void OnFrameUpdated()
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() => UpdateImage()));
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
            _container.Children.Add(_webView);

            var resourcesPath = ResolveResourcesPath();

            _configReader.Logger.Info("[DisplayMode] Initializing WebView2...");
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
            _container.SizeChanged += (_, _) => UpdateWebViewBounds();

            _configReader.Logger.Info("[DisplayMode] WebView2 initialized");
        }

        /// <summary>将 WebView2 HWND 限制为 container 的实际大小（物理像素）</summary>
        private void UpdateWebViewBounds()
        {
            if (_webView?.CoreWebView2 == null) return;

            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var w = _container.ActualWidth;
                var h = _container.ActualHeight;
                if (w <= 0 || h <= 0) return;

                // WPF 设备无关单位 → 物理像素
                var source = PresentationSource.FromVisual(_container);
                var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                var hwnd = new WindowInteropHelper(Window.GetWindow(_container)!).Handle;
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
            return AppPaths.ResourcesDir;
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
                        _configReader.Logger.Info("[DisplayMode] VRM model ready, subscribing to events");
                        SubscribeToVrmEvents();
                        break;
                    case "toggleToolbar":
                        // 工具栏已改为自动隐藏，忽略 VRM 点击切换
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

        /// <summary>向 VRM Viewer 发送 JSON 消息</summary>
        public void SendToViewer(object message)
        {
            try
            {
                var json = JsonSerializer.Serialize(message);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_webView?.CoreWebView2 != null)
                        _webView.CoreWebView2.PostWebMessageAsJson(json);
                });
            }
            catch { }
        }

        /// <summary>清理所有资源</summary>
        public void Dispose()
        {
            _timer?.Stop();

            // GIF 模式清理
            if (!string.IsNullOrEmpty(_moodSubscriptionId))
                _eventDispatcher.Unsubscribe(_moodSubscriptionId);

            // VRM 模式清理
            foreach (var subId in _subscriptionIds)
                _eventDispatcher.Unsubscribe(subId);
            _subscriptionIds.Clear();
            _webView?.Dispose();
        }
    }
}
