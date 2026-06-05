using System.Windows;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 截图功能声明对话框
    /// 首次启动时弹出，告知用户截图用途和隐私保护措施
    /// </summary>
    public partial class ScreenshotConsentDialog : Window
    {
        /// <summary>用户是否同意开启截图功能</summary>
        public bool UserConsented { get; private set; }

        public ScreenshotConsentDialog()
        {
            InitializeComponent();
        }

        private void ConsentButton_Click(object sender, RoutedEventArgs e)
        {
            UserConsented = true;
            DialogResult = true;
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            UserConsented = false;
            DialogResult = false;
        }
    }
}
