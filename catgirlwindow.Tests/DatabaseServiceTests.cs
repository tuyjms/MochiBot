using catgirlwindow.Models;
using catgirlwindow.Services;

namespace catgirlwindow.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly string _connectionString;
    private readonly DatabaseService _db;

    public DatabaseServiceTests()
    {
        // 每个测试使用独立的 SQLite 内存数据库
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mochibot_test_{Guid.NewGuid()}.db");
        _connectionString = $"Data Source={_testDbPath}";
        _db = new DatabaseService(_connectionString);
    }

    public void Dispose()
    {
        // 清理测试数据库文件
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // 忽略清理时的异常
        }
    }

    // ========== 用户配置 ==========

    [Fact]
    public async Task LoadConfig_Default_ShouldReturnDefaultValues()
    {
        var config = await _db.LoadConfigAsync();

        Assert.Equal("小可爱", config.Name);
        Assert.Equal("温柔", config.Personality);
        Assert.Equal(1.0, config.Opacity);
        Assert.True(config.MurmurEnabled);
        Assert.Equal(30, config.MurmurInterval);
    }

    [Fact]
    public async Task SaveAndLoadConfig_ShouldBeConsistent()
    {
        var config = new UserConfig
        {
            Name = "测试女友",
            Personality = "活泼",
            Opacity = 0.8,
            MurmurEnabled = false,
            MurmurInterval = 60,
            WindowPosX = 200,
            WindowPosY = 300
        };

        await _db.SaveConfigAsync(config);
        var loaded = await _db.LoadConfigAsync();

        Assert.Equal(config.Name, loaded.Name);
        Assert.Equal(config.Personality, loaded.Personality);
        Assert.Equal(config.Opacity, loaded.Opacity);
        Assert.Equal(config.MurmurEnabled, loaded.MurmurEnabled);
        Assert.Equal(config.MurmurInterval, loaded.MurmurInterval);
        Assert.Equal(config.WindowPosX, loaded.WindowPosX);
        Assert.Equal(config.WindowPosY, loaded.WindowPosY);
    }

    [Fact]
    public async Task SaveConfig_UpdateExisting_ShouldOverwrite()
    {
        // 第一次保存
        await _db.SaveConfigAsync(new UserConfig { Name = "版本1" });
        // 第二次保存
        await _db.SaveConfigAsync(new UserConfig { Name = "版本2" });

        var loaded = await _db.LoadConfigAsync();
        Assert.Equal("版本2", loaded.Name);
    }

    // ========== 聊天记录 ==========

    [Fact]
    public async Task SaveAndLoadChatHistory_ShouldBeConsistent()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "你好", Timestamp = new DateTime(2026, 1, 1, 10, 0, 0) },
            new() { Role = "assistant", Content = "你好呀～", Timestamp = new DateTime(2026, 1, 1, 10, 0, 5) }
        };

        await _db.SaveChatHistoryAsync(messages);
        var loaded = await _db.LoadChatHistoryAsync(10);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("user", loaded[0].Role);
        Assert.Equal("你好", loaded[0].Content);
        Assert.Equal("assistant", loaded[1].Role);
        Assert.Equal("你好呀～", loaded[1].Content);
    }

    [Fact]
    public async Task LoadChatHistory_ShouldRespectLimit()
    {
        var messages = new List<ChatMessage>();
        for (int i = 0; i < 10; i++)
        {
            messages.Add(new ChatMessage { Role = "user", Content = $"msg{i}" });
        }

        await _db.SaveChatHistoryAsync(messages);
        var loaded = await _db.LoadChatHistoryAsync(limit: 3);

        Assert.Equal(3, loaded.Count);
        Assert.Equal("msg7", loaded[0].Content);
        Assert.Equal("msg9", loaded[2].Content);
    }

    [Fact]
    public async Task LoadChatHistory_Empty_ShouldReturnEmptyList()
    {
        var loaded = await _db.LoadChatHistoryAsync();
        Assert.Empty(loaded);
    }

    // ========== 情绪日志 ==========

    [Fact]
    public async Task LogMood_ShouldBeRetrievable()
    {
        await _db.LogMoodChangeAsync(AgentMood.Happy, "UserCompliment");
        await _db.LogMoodChangeAsync(AgentMood.Sleepy, "LateNight");

        var logs = await _db.GetMoodLogAsync(DateTime.MinValue, DateTime.MaxValue);

        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.Mood == AgentMood.Happy && l.Trigger == "UserCompliment");
        Assert.Contains(logs, l => l.Mood == AgentMood.Sleepy && l.Trigger == "LateNight");
    }

    [Fact]
    public async Task GetMoodLog_ShouldFilterByTimeRange()
    {
        await _db.LogMoodChangeAsync(AgentMood.Happy, "Event1");
        await _db.LogMoodChangeAsync(AgentMood.Sad, "Event2");

        // 查询未来时间范围，应该没有结果
        var futureStart = new DateTime(2099, 1, 1);
        var futureEnd = new DateTime(2099, 12, 31);
        var emptyLogs = await _db.GetMoodLogAsync(futureStart, futureEnd);
        Assert.Empty(emptyLogs);

        // 查询所有时间范围，应该有2条
        var allLogs = await _db.GetMoodLogAsync(DateTime.MinValue, DateTime.MaxValue);
        Assert.Equal(2, allLogs.Count);
    }

    [Fact]
    public async Task GetMoodLog_EmptyRange_ShouldReturnEmpty()
    {
        await _db.LogMoodChangeAsync(AgentMood.Happy, "Test");

        var logs = await _db.GetMoodLogAsync(
            new DateTime(2020, 1, 1),
            new DateTime(2020, 12, 31));

        Assert.Empty(logs);
    }
}
