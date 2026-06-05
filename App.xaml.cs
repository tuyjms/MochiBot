using System.Windows;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Services;
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

            // 创建数据库服务和 Repository
            var configReader = ConfigReader.Instance;
            var databaseService = new DatabaseService();
            var userConfigRepository = new UserConfigRepository(databaseService);

            // 截图声明检查：首次启动时弹出声明对话框
            var moduleSettings = configReader.GetModuleSettings();
            if (!moduleSettings.Vision_ScreenshotConsent)
            {
                var consentDialog = new ScreenshotConsentDialog();
                consentDialog.ShowDialog();
                moduleSettings.Vision_ScreenshotConsent = consentDialog.UserConsented;
                configReader.SaveModuleSettings(moduleSettings);
            }

            // 创建 MainWindow 并传入依赖
            var mainWindow = new MainWindow(Program.EventDispatcher, configReader, userConfigRepository);
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
