using MochiBot.Src.Core.Config;
using Xunit;

namespace MochiBot.Tests.Services
{
    /// <summary>
    /// ConfigReader 单元测试
    /// 使用 TestConfigHelper 共享配置初始化，各测试不再自行 Initialize
    /// </summary>
    [Collection("ConfigReader")]
    public class ConfigReaderTests : IDisposable
    {
        private string? _tempConfigForReload;

        public ConfigReaderTests()
        {
            TestConfigHelper.EnsureInitialized();
        }

        public void Dispose()
        {
            if (_tempConfigForReload != null)
                try { if (File.Exists(_tempConfigForReload)) File.Delete(_tempConfigForReload); } catch { }
        }

        // ========== 提供商配置 ==========

        [Fact]
        public void GetAvailableProviders_ShouldReturnNonEmptyList()
        {
            var providers = ConfigReader.Instance.GetAvailableProviders();
            Assert.NotEmpty(providers);
            Assert.Contains("LocalLMStudio", providers);
        }

        [Fact]
        public void GetProvider_Existing_ShouldReturnConfig()
        {
            var provider = ConfigReader.Instance.GetProvider("LocalLMStudio");
            Assert.NotNull(provider);
            Assert.Equal("http://localhost:1234/v1", provider!.BaseUrl);
        }

        [Fact]
        public void GetProvider_NonExisting_ShouldReturnNull()
        {
            var provider = ConfigReader.Instance.GetProvider("Nonexistent");
            Assert.Null(provider);
        }

        // ========== 应用级配置 ==========

        [Fact]
        public void GetAppSettings_ShouldReturnSettings()
        {
            var settings = ConfigReader.Instance.GetAppSettings();
            Assert.Equal("test", settings.ActivePersonality);
            Assert.True(settings.EnableStructuredResponse);
        }

        // ========== 模块参数配置 ==========

        [Fact]
        public void GetModuleParam_Existing_ShouldReturnValue()
        {
            var value = ConfigReader.Instance.GetModuleParam<int>("ShortTermMemory_Capacity", 50);
            Assert.Equal(50, value);
        }

        [Fact]
        public void GetModuleParam_NonExisting_ShouldReturnDefault()
        {
            var value = ConfigReader.Instance.GetModuleParam("NonExistent", "fallback");
            Assert.Equal("fallback", value);
        }

        // ========== 人格配置 ==========

        [Fact]
        public void GetActivePersonality_ShouldReturnConfig()
        {
            var personality = ConfigReader.Instance.GetActivePersonality();
            Assert.NotNull(personality);
            Assert.Equal("test", personality!.Name);
        }

        [Fact]
        public void GetPersonalityModels_ShouldReturnModelLists()
        {
            var (chatModels, visionModels) = ConfigReader.Instance.GetPersonalityModels("test");
            Assert.NotNull(chatModels);
            Assert.NotEmpty(chatModels);
            Assert.NotNull(visionModels);
            Assert.NotEmpty(visionModels!);
        }

        [Fact]
        public void GetPersonalityModels_WithVision_ShouldReturnModels()
        {
            // 共享配置中 test 人格有 VisionModels，验证能正常读取
            var (_, visionModels) = ConfigReader.Instance.GetPersonalityModels("test");
            Assert.NotNull(visionModels);
        }

        [Fact]
        public void LoadPersonality_NonExisting_ShouldReturnNull()
        {
            var personality = ConfigReader.Instance.LoadPersonality("nonexistent");
            Assert.Null(personality);
        }

        [Fact]
        public void GetAvailablePersonalities_ShouldReturnList()
        {
            var list = ConfigReader.Instance.GetAvailablePersonalities();
            Assert.Contains("test", list);
        }

        // ========== 配置重载 ==========

        [Fact]
        public void Reload_ShouldUpdateConfig()
        {
            // 用测试人格目录创建临时配置（避免 null 回退到项目真实目录）
            var tempPersonalityDir = TestConfigHelper.GetTestPersonalityDir();
            _tempConfigForReload = Path.Combine(Path.GetTempPath(), $"reload_test_{Guid.NewGuid()}.json");
            File.WriteAllText(_tempConfigForReload, """
                {
                    "Providers": {},
                    "AppSettings": { "ActivePersonality": "新人格" },
                    "ModuleSettings": {}
                }
                """);
            ConfigReader.Initialize(_tempConfigForReload, tempPersonalityDir);
            Assert.Equal("新人格", ConfigReader.Instance.GetAppSettings().ActivePersonality);

            // 强制恢复共享配置，避免污染后续测试
            TestConfigHelper.ForceReinitialize();
        }

        // ========== 人物名称合法性检查 ==========

        [Fact]
        public void IsValidPersonalityName_Valid_ShouldReturnTrue()
        {
            Assert.True(ConfigReader.IsValidPersonalityName("小可爱"));
            Assert.True(ConfigReader.IsValidPersonalityName("XiaoKeAi"));
            Assert.True(ConfigReader.IsValidPersonalityName("小可爱_2"));
        }

        [Fact]
        public void IsValidPersonalityName_Invalid_ShouldReturnFalse()
        {
            Assert.False(ConfigReader.IsValidPersonalityName(""));
            Assert.False(ConfigReader.IsValidPersonalityName("123abc"));
            Assert.False(ConfigReader.IsValidPersonalityName("hello world"));
            Assert.False(ConfigReader.IsValidPersonalityName("test@name"));
        }

        // ========== 定时任务 ==========

        [Fact]
        public void GetCronTasks_ShouldReturnList()
        {
            var tasks = ConfigReader.Instance.GetCronTasks();
            Assert.NotNull(tasks);
        }

        [Fact]
        public void GetModuleSettings_ShouldReturnSettings()
        {
            var settings = ConfigReader.Instance.GetModuleSettings();
            Assert.NotNull(settings);
            Assert.Equal(50, settings.ShortTermMemory_Capacity);
        }
    }
}
