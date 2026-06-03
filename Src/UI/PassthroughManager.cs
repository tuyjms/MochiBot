using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MochiBot.Src.Core.Config;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 鼠标穿透管理器
    /// 负责 Win32 穿透模式切换、穿透控制窗口、按钮拖动、透明度控制
    /// </summary>
    public class PassthroughManager
    {
        private readonly Window _owner;
        private readonly IConfigReader _configReader;
        private readonly Func<string> _getDisplayMode;
        private readonly Action<double>? _setVrmContentOpacity;
        private readonly Action<double>? _setToolbarOpacity;
        private readonly Action? _onToggleRequested;

        private bool _isPassthrough;
        private Window? _passthroughWindow;

        // 穿透按钮拖动（相对于主窗口的偏移量）
        private Point _dragStartPoint;
        private bool _isDragging;
        private double _passthroughOffsetX = 4;
        private double _passthroughOffsetY = 4;

        // Win32 鼠标穿透
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOSIZE = 0x0001;

        /// <summary>当前是否处于穿透模式</summary>
        public bool IsPassthrough => _isPassthrough;

        /// <summary>
        /// 创建穿透管理器
        /// </summary>
        /// <param name="owner">所属主窗口</param>
        /// <param name="configReader">配置读取器</param>
        /// <param name="getDisplayMode">获取当前显示模式（"Gif"/"Vrm"）</param>
        /// <param name="setVrmContentOpacity">VRM 模式下设置 WebView2 内容透明度</param>
        /// <param name="setToolbarOpacity">设置工具栏透明度</param>
        /// <param name="onToggleRequested">穿透切换时的回调（用于更新 TrayIcon 菜单勾选状态）</param>
        public PassthroughManager(
            Window owner,
            IConfigReader configReader,
            Func<string> getDisplayMode,
            Action<double>? setVrmContentOpacity = null,
            Action<double>? setToolbarOpacity = null,
            Action? onToggleRequested = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _getDisplayMode = getDisplayMode;
            _setVrmContentOpacity = setVrmContentOpacity;
            _setToolbarOpacity = setToolbarOpacity;
            _onToggleRequested = onToggleRequested;
        }

        /// <summary>设置穿透模式（供设置窗口调用）</summary>
        public void SetPassthrough(bool enable)
        {
            if (enable == _isPassthrough) return;
            TogglePassthrough();
        }

        /// <summary>切换穿透模式</summary>
        public void TogglePassthrough()
        {
            var hwnd = new WindowInteropHelper(_owner).Handle;
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

            _onToggleRequested?.Invoke();
        }

        /// <summary>设置窗口透明度，0.0~1.0</summary>
        public void SetWindowOpacity(double opacity)
        {
            opacity = Math.Clamp(opacity, 0.0, 1.0);
            if (_getDisplayMode() == "Vrm")
            {
                // VRM 模式：JS 设置 WebView2 内容透明度 + WPF 工具栏透明度
                _setVrmContentOpacity?.Invoke(opacity);
                _setToolbarOpacity?.Invoke(opacity);
            }
            else
            {
                // GIF 模式：使用 WPF 原生透明度（整体生效，含工具栏）
                _owner.Opacity = opacity;
            }
        }

        /// <summary>创建穿透模式控制窗口（独立窗口，不被穿透）</summary>
        private void ShowPassthroughWindow()
        {
            if (_passthroughWindow != null) return;

            var pinPath = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse(
                    "M16,12V4h1V2H7v2h1v8l-2,2v2h5.2v6h1.6v-6H18v-2L16,12z"),
                Fill = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF333333")),
                Width = 18,
                Height = 18,
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var btn = new System.Windows.Controls.Button
            {
                Content = pinPath,
                Width = 36,
                Height = 36,
                Background = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // 点击 vs 拖动区分：拖动改变按钮相对于主窗口的位置
            btn.PreviewMouseLeftButtonDown += (_, e) =>
            {
                // 使用屏幕坐标（物理像素），避免窗口移动导致坐标系漂移
                _dragStartPoint = btn.PointToScreen(e.GetPosition(btn));
                _isDragging = false;
                btn.CaptureMouse();
                e.Handled = true;
            };

            btn.PreviewMouseMove += (_, e) =>
            {
                if (!btn.IsMouseCaptured) return;
                var screenPos = btn.PointToScreen(e.GetPosition(btn));
                var dx = screenPos.X - _dragStartPoint.X;
                var dy = screenPos.Y - _dragStartPoint.Y;
                if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
                    _isDragging = true;
                if (_isDragging)
                {
                    var source = PresentationSource.FromVisual(_owner);
                    var dpi = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                    // 屏幕像素增量 → DIP 偏移
                    _passthroughOffsetX += dx / dpi;
                    _passthroughOffsetY += dy / dpi;
                    _passthroughOffsetX = Math.Clamp(_passthroughOffsetX, 0, _owner.Width - 42);
                    _passthroughOffsetY = Math.Clamp(_passthroughOffsetY, 0, _owner.Height - 42);
                    _dragStartPoint = screenPos;
                    // Win32 直接移动，绕过 WPF 渲染管线
                    var pwHwnd = new WindowInteropHelper(_passthroughWindow!).Handle;
                    SetWindowPos(pwHwnd, HWND_TOP,
                        (int)((_owner.Left + _passthroughOffsetX) * dpi),
                        (int)((_owner.Top + _passthroughOffsetY) * dpi),
                        0, 0, SWP_NOSIZE | SWP_NOZORDER);
                }
            };

            btn.PreviewMouseLeftButtonUp += (_, e) =>
            {
                btn.ReleaseMouseCapture();
                if (!_isDragging)
                    TogglePassthrough();
                e.Handled = true;
            };

            // 悬停变色
            btn.MouseEnter += (_, _) =>
            {
                if (btn.Content is System.Windows.Shapes.Path p)
                    p.Fill = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF6B5CE7"));
            };
            btn.MouseLeave += (_, _) =>
            {
                if (btn.Content is System.Windows.Shapes.Path p)
                    p.Fill = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF333333"));
            };

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
                Owner = _owner
            };

            PositionPassthroughWindow();
            _passthroughWindow.Show();

            // 跟随主窗口移动
            _owner.LocationChanged += (_, _) => PositionPassthroughWindow();
        }

        private void PositionPassthroughWindow()
        {
            if (_passthroughWindow == null) return;
            _passthroughWindow.Left = _owner.Left + _passthroughOffsetX;
            _passthroughWindow.Top = _owner.Top + _passthroughOffsetY;
        }

        private void ClosePassthroughWindow()
        {
            if (_passthroughWindow == null) return;
            _passthroughWindow.Close();
            _passthroughWindow = null;
        }

        /// <summary>清理穿透窗口资源</summary>
        public void Dispose()
        {
            ClosePassthroughWindow();
        }
    }
}
