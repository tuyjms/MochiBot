using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 工具栏与气泡消息控制器
    /// 负责工具栏自动隐藏、气泡消息显示/隐藏、图标悬停效果
    /// </summary>
    public class ToolbarController : IDisposable
    {
        private readonly StackPanel _toolbarPanel;
        private readonly Border _toolbarHitZone;
        private readonly Border _chatBubble;
        private readonly TextBlock _bubbleAvatarText;
        private readonly TextBlock _bubbleText;
        private readonly string _defaultAvatarChar;

        private DispatcherTimer? _toolbarHideTimer;
        private DispatcherTimer? _bubbleHideTimer;

        /// <summary>气泡点击时触发（用于打开聊天窗口）</summary>
        public event Action? BubbleClicked;

        public ToolbarController(
            StackPanel toolbarPanel,
            Border toolbarHitZone,
            Border chatBubble,
            TextBlock bubbleAvatarText,
            TextBlock bubbleText,
            string defaultAvatarChar)
        {
            _toolbarPanel = toolbarPanel ?? throw new ArgumentNullException(nameof(toolbarPanel));
            _toolbarHitZone = toolbarHitZone ?? throw new ArgumentNullException(nameof(toolbarHitZone));
            _chatBubble = chatBubble ?? throw new ArgumentNullException(nameof(chatBubble));
            _bubbleAvatarText = bubbleAvatarText ?? throw new ArgumentNullException(nameof(bubbleAvatarText));
            _bubbleText = bubbleText ?? throw new ArgumentNullException(nameof(bubbleText));
            _defaultAvatarChar = defaultAvatarChar;

            InitializeTimers();
            BindToolbarEvents();
        }

        private void InitializeTimers()
        {
            _toolbarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _toolbarHideTimer.Tick += (_, _) =>
            {
                _toolbarPanel.Visibility = Visibility.Collapsed;
                _toolbarHideTimer.Stop();
            };

            _bubbleHideTimer = new DispatcherTimer();
            _bubbleHideTimer.Tick += (_, _) =>
            {
                _bubbleHideTimer.Stop();
                // 淡出动画：Opacity 1→0，300ms，完成后隐藏
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (_, _) =>
                {
                    _chatBubble.Visibility = Visibility.Collapsed;
                    _chatBubble.Opacity = 1; // 重置，下次显示不受影响
                };
                _chatBubble.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            };
        }

        private void BindToolbarEvents()
        {
            _toolbarHitZone.MouseEnter += (_, _) =>
            {
                _toolbarHideTimer?.Stop();
                _toolbarPanel.Visibility = Visibility.Visible;
            };

            _toolbarHitZone.MouseLeave += (_, _) =>
            {
                _toolbarHideTimer?.Start();
            };

            _toolbarPanel.MouseEnter += (_, _) =>
            {
                _toolbarHideTimer?.Stop();
            };

            _toolbarPanel.MouseLeave += (_, _) =>
            {
                _toolbarHideTimer?.Start();
            };
        }

        // ========== 气泡消息 ==========

        /// <summary>显示气泡消息</summary>
        public void ShowBubble(string text, string? avatarChar)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _bubbleAvatarText.Text = string.IsNullOrEmpty(avatarChar) ? _defaultAvatarChar : avatarChar;
                _bubbleText.Text = text;

                // 重置动画状态（如果正在淡出中）
                _chatBubble.BeginAnimation(UIElement.OpacityProperty, null);
                _chatBubble.Opacity = 1;
                _chatBubble.Visibility = Visibility.Visible;

                // 气泡自动消失：2s 基础 + 每 50 字 1s，上限 8s
                var durationSec = Math.Min(2 + text.Length / 50, 8);
                _bubbleHideTimer?.Stop();
                if (_bubbleHideTimer != null)
                {
                    _bubbleHideTimer.Interval = TimeSpan.FromSeconds(durationSec);
                    _bubbleHideTimer.Start();
                }
            });
        }

        /// <summary>隐藏气泡消息</summary>
        public void HideBubble()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _bubbleHideTimer?.Stop();
                _chatBubble.BeginAnimation(UIElement.OpacityProperty, null);
                _chatBubble.Opacity = 1;
                _chatBubble.Visibility = Visibility.Collapsed;
            });
        }

        /// <summary>气泡点击事件处理（XAML 绑定）</summary>
        public void OnBubbleClick(object sender, MouseButtonEventArgs e)
        {
            _bubbleHideTimer?.Stop();
            _chatBubble.BeginAnimation(UIElement.OpacityProperty, null);
            _chatBubble.Opacity = 1;
            _chatBubble.Visibility = Visibility.Collapsed;
            BubbleClicked?.Invoke();
        }

        // ========== 图标悬停效果 ==========

        /// <summary>图标按钮鼠标进入（XAML 绑定）</summary>
        public void IconBtn_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Button btn && btn.Content is System.Windows.Shapes.Path path)
                path.Fill = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF6B5CE7"));
        }

        /// <summary>图标按钮鼠标离开（XAML 绑定）</summary>
        public void IconBtn_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Button btn && btn.Content is System.Windows.Shapes.Path path)
                path.Fill = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF333333"));
        }

        /// <summary>清理定时器资源</summary>
        public void Dispose()
        {
            _toolbarHideTimer?.Stop();
            _bubbleHideTimer?.Stop();
        }
    }
}
