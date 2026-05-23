using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MochiBot.Src.Core.Config;

namespace MochiBot.Src.UI
{
    public partial class VrmViewerWindow : Window
    {
        private readonly IConfigReader _configReader;

        public VrmViewerWindow(IConfigReader configReader)
        {
            _configReader = configReader;
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

                // Check for .vrma motion files and pass the most recent one
                var dataDir = Path.Combine(resourcesPath, "Data");
                var motionParam = "";
                if (Directory.Exists(dataDir))
                {
                    var vrmaFiles = Directory.GetFiles(dataDir, "*.vrma");
                    if (vrmaFiles.Length > 0)
                    {
                        // Use the most recently modified .vrma file
                        var latest = vrmaFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
                        var motionFileName = Path.GetFileName(latest);
                        var encodedMotionPath = Uri.EscapeDataString($"Data/{motionFileName}");
                        motionParam = $"&motion={encodedMotionPath}";
                    }
                }

                var viewerUrl = $"https://vrm.local/Viewer/vrm-viewer.html?model={encodedModelPath}{motionParam}&t={DateTime.Now.Ticks}";

                DebugLog($"Navigating to: {viewerUrl}");
                webView.CoreWebView2.Navigate(viewerUrl);
                _configReader.Logger.Info("[VRMViewer] Navigation started");
                DebugLog("Navigation command issued");
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
            _configReader.Logger.Info("[VRMViewer] Window closed");
            webView.Dispose();
            base.OnClosed(e);
        }
    }
}
