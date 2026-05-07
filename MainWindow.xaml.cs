using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MochiBot.Src.Renderer;

namespace MochiBot
{
    public partial class MainWindow : Window
    {
        private CharacterRenderer _renderer = new();
        private System.Windows.Threading.DispatcherTimer _timer = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
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

                // 启动定时器刷�?
                _timer.Interval = TimeSpan.FromMilliseconds(50);
                _timer.Tick += (s, args) => UpdateImage();
                _timer.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"初始化失�? {ex.Message}");
            }
        }

        private void OnFrameUpdated()
        {
            Dispatcher.Invoke(() => UpdateImage());
        }

        private void UpdateImage()
        {
            var frame = _renderer.CurrentFrame;
            if (frame != null)
            {
                using (var ms = new MemoryStream())
                {
                    frame.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    characterImage.Source = bitmap;
                }
            }
        }
    }
}
