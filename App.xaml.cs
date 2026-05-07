using System.Windows;
using MochiBot.Src.UI;

namespace MochiBot.Src.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 初始化所有依赖
            Program.Initialize();

            // 创建 MainWindow 并传入 EventDispatcher
            var mainWindow = new MainWindow(Program.EventDispatcher);
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 停止事件调度器
            Program.EventDispatcher.StopScheduler();

            base.OnExit(e);
        }
    }
}
