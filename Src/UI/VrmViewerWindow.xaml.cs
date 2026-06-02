using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;

namespace MochiBot.Src.UI
{
    public partial class VrmViewerWindow : Window
    {
        private readonly IConfigReader _configReader;
        private readonly IEventDispatcher? _eventDispatcher;
        private readonly List<string> _subscriptionIds = new();
        private bool _webViewReady;

        public VrmViewerWindow(IConfigReader configReader, IEventDispatcher? eventDispatcher = null)
        {
            _configReader = configReader;
            _eventDispatcher = eventDispatcher;
            InitializeComponent();
            Loaded += OnLoaded;
            DebugLog("Constructor done, Loaded subscribed");
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            DebugLog("OnLoaded started");
            try
            {
                var resourcesPath = ResolveResourcesPath();
                DebugLog($"Resources path: {resourcesPath}");
                DebugLog($"Viewer HTML exists: {File.Exists(Path.Combine(resourcesPath, "Viewer", "vrm-viewer.html"))}");
                DebugLog($"VRM file exists: {File.Exists(Path.Combine(resourcesPath, "Data", "QQ vrm 1.vrm"))}");

                _configReader.Logger.Info("[VRMViewer] Initializing WebView2...");
                DebugLog("Calling EnsureCoreWebView2Async...");

                await webView.EnsureCoreWebView2Async(null);

                DebugLog("WebView2 Core ready, setting up virtual host...");

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "vrm.local",
                    resourcesPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                DebugLog("Virtual host mapping set");

                // 捕获导航完成和内部错误
                webView.CoreWebView2.NavigationCompleted += (s, args) =>
                {
                    DebugLog($"Navigation completed: IsSuccess={args.IsSuccess}, Status={args.WebErrorStatus}");
                    if (!args.IsSuccess)
                        _configReader.Logger.Error($"[VRMViewer] Navigation failed: {args.WebErrorStatus}");
                };
                webView.CoreWebView2.ProcessFailed += (s, args) =>
                {
                    DebugLog($"Process failed: Reason={args.Reason}");
                    _configReader.Logger.Error($"[VRMViewer] Process failed: {args.Reason}");
                };

                // Register handler for .vrm and .vrma files (binary glTF formats)
                webView.CoreWebView2.AddWebResourceRequestedFilter("https://vrm.local/*.vrm", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.AddWebResourceRequestedFilter("https://vrm.local/*.vrma", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.WebResourceRequested += (s, args) =>
                {
                    var localPath = args.Request.Uri.Replace("https://vrm.local/", resourcesPath + "\\");
                    localPath = Uri.UnescapeDataString(localPath);
                    DebugLog($"WebResource requested: {args.Request.Uri} -> {localPath} (exists={File.Exists(localPath)})");
                    if (File.Exists(localPath))
                    {
                        var bytes = File.ReadAllBytes(localPath);
                        var stream = new MemoryStream(bytes);
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            stream, 200, "OK", "Content-Type: application/octet-stream\r\n");
                    }
                };

                var modelFileName = "QQ vrm 1.vrm";
                var encodedModelPath = Uri.EscapeDataString($"Data/{modelFileName}");

                var viewerUrl = $"https://vrm.local/Viewer/vrm-viewer.html?model={encodedModelPath}&t={DateTime.Now.Ticks}";

                DebugLog($"Navigating to: {viewerUrl}");
                webView.CoreWebView2.Navigate(viewerUrl);
                _configReader.Logger.Info("[VRMViewer] Navigation started");
                DebugLog("Navigation command issued");

                // 监听 JS 端 postMessage（模型加载完成时 JS 会发送 ready 消息）
                webView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    try
                    {
                        var json = args.WebMessageAsJson;
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("type", out var typeProp) &&
                            typeProp.GetString() == "ready")
                        {
                            _webViewReady = true;
                            DebugLog("VRM model ready, subscribing to events");
                            SubscribeToEvents();
                        }
                    }
                    catch { }
                };
            }
            catch (Exception ex)
            {
                DebugLog($"EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                _configReader.Logger.Error($"[VRMViewer] Init failed: {ex}");
                MessageBox.Show($"VRM Viewer 初始化失败:\n{ex.GetType().Name}: {ex.Message}",
                    "VRM Viewer - 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

        /// <summary>订阅心情变化和模块状态事件</summary>
        private void SubscribeToEvents()
        {
            if (_eventDispatcher == null) return;

            // 订阅心情变化事件
            var moodSubId = _eventDispatcher.Subscribe(EventCategory.MoodChange, OnMoodChange);
            _subscriptionIds.Add(moodSubId);

            // 订阅模块状态变更事件
            var stateSubId = _eventDispatcher.Subscribe(EventCategory.ModuleState, OnModuleStateChanged);
            _subscriptionIds.Add(stateSubId);

            _configReader.Logger.Info("[VRMViewer] 已订阅 MoodChange 和 ModuleState 事件");
        }

        /// <summary>心情变化 → VRM 表情</summary>
        private void OnMoodChange(EventData eventData)
        {
            if (!_webViewReady) return;
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                var root = doc.RootElement;

                // 有 animation 字段时跳过（由 2D 渲染器处理）
                if (root.TryGetProperty("animation", out _)) return;

                if (root.TryGetProperty("mood", out var moodProp))
                {
                    var mood = moodProp.GetString()?.ToLower();
                    if (!string.IsNullOrEmpty(mood))
                    {
                        SendToViewer(new { type = "mood", expression = mood });
                    }
                }
            }
            catch { }
        }

        /// <summary>Agent 状态变化 → VRM 表情/视线</summary>
        private void OnModuleStateChanged(EventData eventData)
        {
            if (!_webViewReady) return;
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                var root = doc.RootElement;

                // 只处理 agent 模块的状态变更
                var moduleId = root.TryGetProperty("moduleId", out var idProp) ? idProp.GetString() : null;
                if (moduleId != "agent") return;

                var state = root.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
                if (!string.IsNullOrEmpty(state))
                {
                    SendToViewer(new { type = "state", state });
                }
            }
            catch { }
        }

        /// <summary>向 WebView2 JS 端发送 JSON 消息</summary>
        private void SendToViewer(object message)
        {
            try
            {
                var json = JsonSerializer.Serialize(message);
                DebugLog($"SendToViewer: {json}");
                Dispatcher.Invoke(() =>
                {
                    if (webView.CoreWebView2 != null)
                        webView.CoreWebView2.PostWebMessageAsJson(json);
                });
            }
            catch (Exception ex)
            {
                DebugLog($"SendToViewer error: {ex.Message}");
            }
        }

        private static void DebugLog(string msg)
        {
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "mochibot_vrm_debug.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 取消事件订阅
            if (_eventDispatcher != null)
            {
                foreach (var subId in _subscriptionIds)
                    _eventDispatcher.Unsubscribe(subId);
                _subscriptionIds.Clear();
            }

            _configReader.Logger.Info("[VRMViewer] Window closed");
            webView.Dispose();
            base.OnClosed(e);
        }
    }
}
