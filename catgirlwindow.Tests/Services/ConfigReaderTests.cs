using catgirlwindow.Src.Services.Config;
using catgirlwindow.Src.Services.Config.Models;

namespace catgirlwindow.SrcTests;

public class ConfigReaderTests : IDisposable
{
    private readonly string _testConfigPath;
    private readonly string _personalitiesDir;

    public ConfigReaderTests()
    {
        _testConfigPath = Path.Combine(Path.GetTempPath(), $"appsettings_test_{Guid.NewGuid()}.json");
        _personalitiesDir = Path.Combine(Path.GetTempPath(), $"Personalities_{Guid.NewGuid()}");

        // 创建测试 appsettings.json（含 ContextLimit）
        File.WriteAllText(_testConfigPath, """
        {
            "Providers": {
                "TestProvider": {
                    "ApiKey": "test-key",
                    "BaseUrl": "http://test.local/v1",
                    "ContextLimit": 8192
                }
            },
            "AppSettings": {
                "ActivePersonality": "测试人格",
                "EnableStructuredResponse": true
            },
            "ModuleSettings": {
                "ShortTermMemory_Capacity": 100,
                "AutoEvent_MurmurInterval": 60
            }
        }
        """);

        // 创建测试人格配置文件
        Directory.CreateDirectory(_personalitiesDir);
        File.WriteAllText(Path.Combine(_personalitiesDir, "测试人格_person.json"), """
        {
            "name": "测试人格",
            "description": "测试用的人格配置",
            "personalities": [
                {
                    "name": "温柔",
                    "description": "温柔体贴",
                    "chatModels": ["TestProvider/test-model", "TestProvider/fallback-model"],
                    "visionModels": ["TestProvider/vision-model"]
                },
                {
                    "name": "毒舌",
                    "description": "毒舌模式",
                    "chatModels": ["TestProvider/test-model"]
                }
            ]
        }
        """);

        ConfigReader.Initialize(_testConfigPath, _personalitiesDir);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testConfigPath)) File.Delete(_testConfigPath);
            if (Directory.Exists(_personalitiesDir))
                Directory.Delete(_personalitiesDir, true);
        }
        catch { }
    }

    /// <summary>
    /// 辅助方法：重新初始化 ConfigReader（用于测试间重置单例状态）
    /// </summary>
    private void Reinitialize()
    {
        ConfigReader.Initialize(_testConfigPath, _personalitiesDir);
    }

    // ========== 提供商配置 ==========

    [Fact]
    public void GetAvailableProviders_ShouldReturnNonEmptyList()
    {
        var providers = ConfigReader.Instance.GetAvailableProviders();
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void GetProvider_Existing_ShouldReturnConfig()
    {
        var provider = ConfigReader.Instance.GetProvider("TestProvider");
        Assert.NotNull(provider);
        Assert.Equal("test-key", provider.ApiKey);
        Assert.Equal("http://test.local/v1", provider.BaseUrl);
        Assert.Equal(8192, provider.ContextLimit);
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
        Assert.True(settings.ActivePersonality == "测试人格", "ActivePersonality should be '测试人格'");
        Assert.True(settings.EnableStructuredResponse);
    }

    // ========== 模块参数配置 ==========

    [Fact]
    public void GetModuleParam_Existing_ShouldReturnValue()
    {
        var capacity = ConfigReader.Instance.GetModuleParam<int>("ShortTermMemory_Capacity", 50);
        Assert.Equal(100, capacity);
    }

    [Fact]
    public void GetModuleParam_NonExisting_ShouldReturnDefault()
    {
        var value = ConfigReader.Instance.GetModuleParam("NonExistent", "default");
        Assert.Equal("default", value);
    }

    // ========== 人格配置 ==========

    [Fact]
    public void GetActivePersonality_ShouldReturnConfig()
    {
        // 先检查 AppSettings 是否正确加载
        var settings = ConfigReader.Instance.GetAppSettings();
        Assert.True(settings.ActivePersonality == "测试人格", "ActivePersonality should be '测试人格'");

        var personality = ConfigReader.Instance.GetActivePersonality();
        Assert.NotNull(personality);
        Assert.True(personality.Name == "测试人格", "Personality name should be '测试人格'");
        Assert.True(personality.Personalities.Count == 2, "Should have 2 sub-personalities");
    }

    [Fact]
    public void GetPersonalityModels_ShouldReturnModelLists()
    {
        var (chatModels, visionModels) = ConfigReader.Instance.GetPersonalityModels("温柔");
        Assert.Equal(2, chatModels.Count);
        Assert.Equal("TestProvider/test-model", chatModels[0]);
        Assert.Equal("TestProvider/fallback-model", chatModels[1]);
        Assert.NotNull(visionModels);
        Assert.Single(visionModels);
    }

    [Fact]
    public void GetPersonalityModels_WithoutVision_ShouldReturnNull()
    {
        var (chatModels, visionModels) = ConfigReader.Instance.GetPersonalityModels("毒舌");
        Assert.Single(chatModels);
        Assert.Equal("TestProvider/test-model", chatModels[0]);
        Assert.Null(visionModels);
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
        Assert.True(list.Contains("测试人格"), "List should contain '测试人格'");
    }

    // ========== 配置重载 ==========

    [Fact]
    public void Reload_ShouldUpdateConfig()
    {
        File.WriteAllText(_testConfigPath, """
        {
            "Providers": {},
            "AppSettings": { "ActivePersonality": "新人格" },
            "ModuleSettings": {}
        }
        """);
        ConfigReader.Instance.Reload();
        Assert.Equal("新人格", ConfigReader.Instance.GetAppSettings().ActivePersonality);

        // 恢复原始配置，避免污染后续测试
        Reinitialize();
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
        Assert.False(ConfigReader.IsValidPersonalityName("123abc"));  // 数字开头
        Assert.False(ConfigReader.IsValidPersonalityName("hello world"));  // 含空格
        Assert.False(ConfigReader.IsValidPersonalityName("test@name"));  // 含特殊字符
    }

    // ========== 异常情况 ==========

    [Fact]
    public void Initialize_NonExistentFile_ShouldThrow()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ConfigReader.Initialize("nonexistent.json"));
    }
}
