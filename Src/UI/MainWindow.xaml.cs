using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Events;
using MochiBot.Src.Services;

namespace MochiBot.Src.UI
{
    public partial class MainWindow : Window
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;
        private readonly UserConfigRepository? _userConfigRepository;
        private ChatWindow? _chatWindow;
        private Window? _settingsWindow;
        private TrayIcon? _trayIcon;
        private string? _latestAgentMessage;

        // 子模块
        private DisplayModeManager _displayModeManager;
        private PassthroughManager _passthroughManager;
        private ToolbarController _toolbarController;

        private string _avatarChar = "宠";

        /// <summary>当前是否处于穿透模式</summary>
        public bool IsPassthrough => _passthroughManager.IsPassthrough;

        /// <summary>设置穿透模式（供 SettingsWindow 调用）</summary>
        public void SetPassthrough(bool enable) => _passthroughManager.SetPassthrough(enable);

        /// <summary>设置窗口透明度（供 SettingsWindow 调用）</summary>
        public void SetWindowOpacity(double opacity) => _passthroughManager.SetWindowOpacity(opacity);

        public MainWindow(IEventDispatcher eventDispatcher, IConfigReader configReader, UserConfigRepository? userConfigRepository = null)
        {
            _eventDispatcher = eventDispatcher;
            _configReader = configReader;
            _userConfigRepository = userConfigRepository;
            InitializeComponent();

            // 窗口尺寸：桌面高度 1/3，按角色比例
            var workArea = SystemParameters.WorkArea;
            Height = workArea.Height / 3.0;
            Width = Height * (512.0 / 689.0);
            Left = workArea.Right - Width;
            Top = workArea.Bottom - Height;

            // 创建子模块
            _displayModeManager = new DisplayModeManager(characterContainer, _configReader, _eventDispatcher);
            _passthroughManager = new PassthroughManager(
                this, _configReader,
                () => _displayModeManager.DisplayMode,
                opacity => _displayModeManager.SendToViewer(new { type = "opacity", value = opacity }),
                opacity => toolbarPanel.Opacity = opacity);
            _toolbarController = new ToolbarController(
                toolbarPanel, toolbarHitZone, chatBubble,
                bubbleAvatarText, bubbleText, _avatarChar);

            // 气泡点击 → 打开聊天窗口
            _toolbarController.BubbleClicked += () => ToggleChatButton_Click(this, new RoutedEventArgs());

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var personality = _configReader.GetActivePersonality();
                var characterName = personality?.Name ?? "宠";
                _avatarChar = characterName.Length > 0 ? characterName[..1] : "宠";

                // 创建聊天窗口
                _chatWindow = new ChatWindow(_eventDispatcher, _configReader, MochiBot.Program.ChatHistoryRepo!);
                _chatWindow.NewAgentMessage += OnChatWindowNewMessage;
                _chatWindow.Owner = this;

                // 初始化显示模式（GIF/VRM）
                await _displayModeManager.InitializeAsync(personality);

                _configReader.Logger.Info($"[MainWindow] 已初始化，显示模式={_displayModeManager.DisplayMode}");
            }
            catch (Exception ex)
            {
                _configReader.Logger.Error($"[MainWindow] 初始化失败: {ex.Message}");
            }
        }

        // ========== 系统托盘 ==========

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var menuItems = new TrayIcon.MenuItem[]
            {
                new("鼠标穿透", () => Dispatcher.Invoke(() => _passthroughManager.TogglePassthrough()), () => _passthroughManager.IsPassthrough),
                new("设置", () => Dispatcher.Invoke(() => SettingsButton_Click(this, new RoutedEventArgs()))),
                new("聊天", () => Dispatcher.Invoke(() => ToggleChatButton_Click(this, new RoutedEventArgs())))
            };
            _trayIcon = new TrayIcon(hwnd, "MochiBot", RestoreFromTray, menuItems);
            _trayIcon.Show();
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

        private void RestoreFromTray()
        {
            Show();
            Activate();
            _configReader.Logger.Info("[MainWindow] 已从系统托盘恢复");
        }

        // ========== UI 事件 ==========

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        // ========== 气泡消息委托（XAML 绑定 → ToolbarController） ==========

        private void Bubble_Click(object sender, MouseButtonEventArgs e)
        {
            _toolbarController.OnBubbleClick(sender, e);
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
                    _toolbarController.ShowBubble(_latestAgentMessage ?? "", avatarChar);
                }
            });
        }

        // ========== 图标悬停效果委托（XAML 绑定 → ToolbarController） ==========

        private void IconBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            _toolbarController.IconBtn_MouseEnter(sender, e);
        }

        private void IconBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            _toolbarController.IconBtn_MouseLeave(sender, e);
        }

        // ========== 工具栏自动隐藏委托（XAML 绑定 → ToolbarController） ==========

        // 注：toolbarHitZone/toolbarPanel 的 MouseEnter/Leave 已在 ToolbarController 构造函数中绑定
        // XAML 中的事件绑定指向此处的空壳方法，实际逻辑由 ToolbarController 处理
        private void toolbarHitZone_MouseEnter(object sender, MouseEventArgs e) { }
        private void toolbarHitZone_MouseLeave(object sender, MouseEventArgs e) { }
        private void toolbarPanel_MouseEnter(object sender, MouseEventArgs e) { }
        private void toolbarPanel_MouseLeave(object sender, MouseEventArgs e) { }

        // ========== 窗口管理 ==========

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
                _toolbarController.HideBubble();
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new SettingsWindow(_configReader, _eventDispatcher, this, _userConfigRepository);
            _settingsWindow.Owner = this;
            _settingsWindow.ShowDialog();
            _settingsWindow = null;
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
                // 清理子模块
                _passthroughManager.Dispose();
                _displayModeManager.Dispose();
                _toolbarController.Dispose();

                _trayIcon?.Dispose();
                _eventDispatcher.StopScheduler();
                Application.Current.Shutdown();
            }
        }
    }
}
