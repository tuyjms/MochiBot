using MochiBot.Src.Core.Config;

namespace MochiBot.Tests
{
    /// <summary>
    /// 测试用共享 ConfigHelper — 管理测试共用的 ConfigReader 单例
    /// 所有需要 ConfigReader 的测试不再各自初始化，而是依赖此 Helper
    /// </summary>
    public static class TestConfigHelper
    {
        private static string? _testConfigPath;
        private static string? _testPersonalityDir;
        private static bool _initialized;

        /// <summary>
        /// 确保 ConfigReader 已用测试配置初始化（幂等）
        /// </summary>
        public static void EnsureInitialized()
        {
            if (_initialized) return;

            _testConfigPath = Path.Combine(Path.GetTempPath(), $"mochibot_test_{Guid.NewGuid()}.json");
            _testPersonalityDir = Path.Combine(Path.GetTempPath(), $"mochibot_test_pers_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testPersonalityDir);

            File.WriteAllText(_testConfigPath, """
                {
                    "Providers": {
                        "LocalLMStudio": {
                            "ApiKey": "not-needed",
                            "BaseUrl": "http://localhost:1234/v1",
                            "ContextLimit": 4096
                        },
                        "OpenAI": {
                            "ApiKey": "test-key",
                            "BaseUrl": "https://api.openai.com/v1",
                            "ContextLimit": 8192
                        }
                    },
                    "AppSettings": {
                        "UserName": "测试用户",
                        "ActivePersonality": "test",
                        "EnableStructuredResponse": true,
                        "MaxActionsPerResponse": 5,
                        "EnableMidTermMemoryOnChat": true,
                        "EnableLongTermRecall": true,
                        "LogLevel": "Info",
                        "LogToFile": true,
                        "LogToConsole": false
                    },
                    "ModuleSettings": {
                        "LlmModel": "default",
                        "LogToFile": true,
                        "LogToConsole": false,
                        "EnableStructuredResponse": true,
                        "ShortTermMemory_OverflowStrategy": "Truncate",
                        "ShortTermMemory_MaxMessages": 50
                    },
                    "CronTasks": []
                }
                """);

            File.WriteAllText(Path.Combine(_testPersonalityDir, "test_person.json"), """
                {
                    "Name": "test",
                    "Description": "测试人格",
                    "Personalities": [],
                    "ChatModels": ["gpt-4", "gpt-3.5-turbo"],
                    "VisionModels": ["gpt-4-vision"]
                }
                """);

            ConfigReader.Initialize(_testConfigPath, _testPersonalityDir);
            _initialized = true;
        }

        /// <summary>
        /// 强制重新初始化（用于恢复被其他测试修改的单例状态）
        /// </summary>
        public static void ForceReinitialize()
        {
            _initialized = false;
            EnsureInitialized();
        }

        /// <summary>
        /// 获取测试人格目录路径
        /// </summary>
        public static string GetTestPersonalityDir()
        {
            EnsureInitialized();
            return _testPersonalityDir!;
        }

        /// <summary>
        /// 清理测试配置文件并重置状态
        /// </summary>
        public static void Cleanup()
        {
            if (_testConfigPath != null)
                TryDelete(_testConfigPath);
            if (_testPersonalityDir != null && Directory.Exists(_testPersonalityDir))
            {
                try { Directory.Delete(_testPersonalityDir, true); } catch { }
            }
            _initialized = false;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
