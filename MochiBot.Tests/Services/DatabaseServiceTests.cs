using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Database.Models;
using MochiBot.Src.EventModels;
using static MochiBot.Src.EventModels.MoodEventTypes;
using MochiBot.Src.Services;
namespace MochiBot.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseService _db;
    private readonly UserConfigRepository _userConfigRepo;
    private readonly ChatHistoryRepository _chatHistoryRepo;
    private readonly MoodLogRepository _moodLogRepo;

    public DatabaseServiceTests()
    {
        // 每个测试使用独立的 SQLite 数据库文件
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mochibot_test_{Guid.NewGuid()}.db");
        var connectionString = $"Data Source={_testDbPath}";
        _db = new DatabaseService(connectionString);
        _userConfigRepo = new UserConfigRepository(_db);
        _chatHistoryRepo = new ChatHistoryRepository(_db);
        _moodLogRepo = new MoodLogRepository(_db);
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
        var config = await _userConfigRepo.LoadConfigAsync();

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
            Name = "测试",
            Personality = "活泼",
            Opacity = 0.8,
            MurmurEnabled = false,
            MurmurInterval = 60,
            WindowPosX = 200,
            WindowPosY = 300
        };

        await _userConfigRepo.SaveConfigAsync(config);
        var loaded = await _userConfigRepo.LoadConfigAsync();

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
        await _userConfigRepo.SaveConfigAsync(new UserConfig { Name = "版本1" });
        // 第二次保存
        await _userConfigRepo.SaveConfigAsync(new UserConfig { Name = "版本2" });

        var loaded = await _userConfigRepo.LoadConfigAsync();
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

        await _chatHistoryRepo.SaveChatHistoryAsync(messages);
        var loaded = await _chatHistoryRepo.LoadChatHistoryAsync(10);

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
            messages.Add(new ChatMessage { Role = "user", Content = $"msg{i}", Timestamp = DateTime.Now });
        }

        await _chatHistoryRepo.SaveChatHistoryAsync(messages);
        var loaded = await _chatHistoryRepo.LoadChatHistoryAsync(limit: 3);

        Assert.Equal(3, loaded.Count);
        Assert.Equal("msg7", loaded[0].Content);
        Assert.Equal("msg9", loaded[2].Content);
    }

    [Fact]
    public async Task LoadChatHistory_Empty_ShouldReturnEmptyList()
    {
        var loaded = await _chatHistoryRepo.LoadChatHistoryAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task SaveSingleMessage_ShouldAppendToExistingData()
    {
        // 先批量写入2条
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "第一条", Timestamp = new DateTime(2026, 1, 1, 10, 0, 0) },
            new() { Role = "assistant", Content = "回复", Timestamp = new DateTime(2026, 1, 1, 10, 0, 5) }
        };
        await _chatHistoryRepo.SaveChatHistoryAsync(messages);

        // 增量追加1条
        await _chatHistoryRepo.SaveSingleMessageAsync(new ChatMessage
        {
            Role = "user",
            Content = "增量消息",
            Timestamp = new DateTime(2026, 1, 1, 10, 1, 0)
        });

        var loaded = await _chatHistoryRepo.LoadChatHistoryAsync(limit: 10);

        Assert.Equal(3, loaded.Count);
        Assert.Equal("第一条", loaded[0].Content);
        Assert.Equal("回复", loaded[1].Content);
        Assert.Equal("增量消息", loaded[2].Content);
    }

    [Fact]
    public async Task SaveSingleMessage_Multiple_ShouldPreserveOrder()
    {
        for (int i = 0; i < 5; i++)
        {
            await _chatHistoryRepo.SaveSingleMessageAsync(new ChatMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"消息{i}",
                Timestamp = new DateTime(2026, 1, 1, 10, 0, i)
            });
        }

        var loaded = await _chatHistoryRepo.LoadChatHistoryAsync(limit: 10);

        Assert.Equal(5, loaded.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"消息{i}", loaded[i].Content);
        }
    }

    // ========== 情绪日志 ==========

    [Fact]
    public async Task LogMood_ShouldBeRetrievable()
    {
        await _moodLogRepo.LogMoodChangeAsync(AgentMood.Happy, "UserCompliment");
        await _moodLogRepo.LogMoodChangeAsync(AgentMood.Sleepy, LateNight);

        var logs = await _moodLogRepo.GetMoodLogAsync(DateTime.MinValue, DateTime.MaxValue);

        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.Mood == AgentMood.Happy && l.Trigger == "UserCompliment");
        Assert.Contains(logs, l => l.Mood == AgentMood.Sleepy && l.Trigger == LateNight);
    }

    [Fact]
    public async Task GetMoodLog_ShouldFilterByTimeRange()
    {
        await _moodLogRepo.LogMoodChangeAsync(AgentMood.Happy, "Event1");
        await _moodLogRepo.LogMoodChangeAsync(AgentMood.Sad, "Event2");

        // 查询未来时间范围，应该没有结果
        var futureStart = new DateTime(2099, 1, 1);
        var futureEnd = new DateTime(2099, 12, 31);
        var emptyLogs = await _moodLogRepo.GetMoodLogAsync(futureStart, futureEnd);
        Assert.Empty(emptyLogs);

        // 查询所有时间范围，应该有2条
        var allLogs = await _moodLogRepo.GetMoodLogAsync(DateTime.MinValue, DateTime.MaxValue);
        Assert.Equal(2, allLogs.Count);
    }

    [Fact]
    public async Task GetMoodLog_EmptyRange_ShouldReturnEmpty()
    {
        await _moodLogRepo.LogMoodChangeAsync(AgentMood.Happy, "Test");

        var logs = await _moodLogRepo.GetMoodLogAsync(
            new DateTime(2020, 1, 1),
            new DateTime(2020, 12, 31));

        Assert.Empty(logs);
    }
}
