using System.IO;
using MochiBot.Src.Agent;
using MochiBot.Src.Core;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Events;
using MochiBot.Src.Services;
using MochiBot.Src.Services.Tool;

namespace MochiBot
{
    /// <summary>
    /// 程序入口点
    /// 负责初始化所有依赖并启动 WPF 应用
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// 全局事件调度器实例
        /// </summary>
        public static IEventDispatcher EventDispatcher { get; } = new EventDispatcher();

        /// <summary>
        /// Agent 实例
        /// </summary>
        public static MainAgent? Agent { get; private set; }

        /// <summary>
        /// 聊天记录仓库实例（供 ChatWindow 使用）
        /// </summary>
        public static ChatHistoryRepository? ChatHistoryRepo { get; private set; }

        /// <summary>
        /// 初始化所有依赖（由 WPF 生成的 Main 调用）
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // 初始化配置读取器
                var configPath = Path.Combine(AppPaths.ExeDirectory, "Resources", "appsettings.json");
                ConfigReader.Initialize(configPath);
                var configReader = ConfigReader.Instance;

                // 创建依赖（Agent 自管理 LlmClient 和 ShortTermMemory）
                var toolService = new ToolService(configReader);
                var databaseService = new DatabaseService();
                ChatHistoryRepo = new ChatHistoryRepository(databaseService);
                var moodLogRepo = new MoodLogRepository(databaseService);

                // 创建 Agent（传入同一个 EventDispatcher）
                Agent = new MainAgent(
                    EventDispatcher,
                    configReader,
                    toolService,
                    ChatHistoryRepo,
                    moodLogRepo);

                // 启动事件调度器定时任务
                EventDispatcher.StartScheduler();

                // 注册内置定时任务
                var builtinHandler = new BuiltinTaskHandler(EventDispatcher);
                var builtinInitializer = new BuiltinTaskInitializer(EventDispatcher, configReader);
                builtinInitializer.Initialize();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Main] 初始化失败: {ex.Message}");
            }
        }
    }
}
