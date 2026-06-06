using MochiBot.Src.Core.Database;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;

namespace MochiBot.Tests.Services;

public class ChatHistoryRepositoryTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly ChatHistoryRepository _repo;

    public ChatHistoryRepositoryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mochibot_chat_test_{Guid.NewGuid()}.db");
        var db = new DatabaseService($"Data Source={_testDbPath}");
        _repo = new ChatHistoryRepository(db);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);
        }
        catch { }
    }

    private async Task SeedMessagesAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await _repo.SaveSingleMessageAsync(new ChatMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"测试消息{i}",
                Timestamp = new DateTime(2026, 1, 1, 10, 0, i)
            });
        }
    }

    // ========== SearchMessagesAsync ==========

    [Fact]
    public async Task SearchMessages_ShouldMatchKeyword()
    {
        await _repo.SaveSingleMessageAsync(new ChatMessage
        {
            Role = "user", Content = "你好世界", Timestamp = new DateTime(2026, 1, 1, 10, 0, 0)
        });
        await _repo.SaveSingleMessageAsync(new ChatMessage
        {
            Role = "assistant", Content = "你好呀", Timestamp = new DateTime(2026, 1, 1, 10, 0, 1)
        });
        await _repo.SaveSingleMessageAsync(new ChatMessage
        {
            Role = "user", Content = "再见", Timestamp = new DateTime(2026, 1, 1, 10, 0, 2)
        });

        var results = await _repo.SearchMessagesAsync("你好");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Message.Content == "你好世界");
        Assert.Contains(results, r => r.Message.Content == "你好呀");
    }

    [Fact]
    public async Task SearchMessages_NoMatch_ShouldReturnEmpty()
    {
        await SeedMessagesAsync(3);

        var results = await _repo.SearchMessagesAsync("不存在的关键词");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchMessages_ShouldReturnIds()
    {
        await _repo.SaveSingleMessageAsync(new ChatMessage
        {
            Role = "user", Content = "带Id的消息", Timestamp = new DateTime(2026, 1, 1, 10, 0, 0)
        });

        var results = await _repo.SearchMessagesAsync("带Id");

        Assert.Single(results);
        Assert.True(results[0].Id > 0);
    }

    [Fact]
    public async Task SearchMessages_ShouldRespectLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            await _repo.SaveSingleMessageAsync(new ChatMessage
            {
                Role = "user", Content = $"关键词{i}", Timestamp = new DateTime(2026, 1, 1, 10, 0, i)
            });
        }

        var results = await _repo.SearchMessagesAsync("关键词", limit: 3);

        Assert.Equal(3, results.Count);
    }

    // ========== DeleteMessageByIdAsync ==========

    [Fact]
    public async Task DeleteMessageById_ShouldRemoveMessage()
    {
        await _repo.SaveSingleMessageAsync(new ChatMessage
        {
            Role = "user", Content = "要删除的消息", Timestamp = new DateTime(2026, 1, 1, 10, 0, 0)
        });
        await _repo.SaveSingleMessageAsync(new ChatMessage
        {
            Role = "user", Content = "保留的消息", Timestamp = new DateTime(2026, 1, 1, 10, 0, 1)
        });

        var searchResults = await _repo.SearchMessagesAsync("要删除的");
        Assert.Single(searchResults);
        var idToDelete = searchResults[0].Id;

        await _repo.DeleteMessageByIdAsync(idToDelete);

        var remaining = await _repo.LoadChatHistoryAsync(100);
        Assert.Single(remaining);
        Assert.Equal("保留的消息", remaining[0].Content);
    }

    [Fact]
    public async Task DeleteMessageById_NonExistent_ShouldNotThrow()
    {
        await SeedMessagesAsync(2);

        // 删除不存在的 Id，不应抛异常
        await _repo.DeleteMessageByIdAsync(99999);

        var remaining = await _repo.LoadChatHistoryAsync(100);
        Assert.Equal(2, remaining.Count);
    }

    // ========== DeleteAllMessagesAsync ==========

    [Fact]
    public async Task DeleteAllMessages_ShouldClearAll()
    {
        await SeedMessagesAsync(5);

        await _repo.DeleteAllMessagesAsync();

        var remaining = await _repo.LoadChatHistoryAsync(100);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteAllMessages_EmptyTable_ShouldNotThrow()
    {
        // 空表清空不应抛异常
        await _repo.DeleteAllMessagesAsync();

        var remaining = await _repo.LoadChatHistoryAsync(100);
        Assert.Empty(remaining);
    }

    // ========== LoadChatHistoryWithIdAsync ==========

    [Fact]
    public async Task LoadWithId_ShouldReturnIds()
    {
        await SeedMessagesAsync(3);

        var results = await _repo.LoadChatHistoryWithIdAsync(limit: 10);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Id > 0));
    }

    [Fact]
    public async Task LoadWithId_ShouldPreserveOrder()
    {
        await SeedMessagesAsync(5);

        var results = await _repo.LoadChatHistoryWithIdAsync(limit: 10);

        Assert.Equal(5, results.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"测试消息{i}", results[i].Message.Content);
        }
    }

    [Fact]
    public async Task LoadWithId_ShouldSupportPagination()
    {
        await SeedMessagesAsync(10);

        // 第一页：最新的 3 条（offset=0，按 Id DESC 取后反转）
        var page1 = await _repo.LoadChatHistoryWithIdAsync(limit: 3, offset: 0);
        Assert.Equal(3, page1.Count);
        Assert.Equal("测试消息7", page1[0].Message.Content);

        // 第二页：接下来的 3 条
        var page2 = await _repo.LoadChatHistoryWithIdAsync(limit: 3, offset: 3);
        Assert.Equal(3, page2.Count);
        Assert.Equal("测试消息4", page2[0].Message.Content);
    }

    [Fact]
    public async Task LoadWithId_Empty_ShouldReturnEmpty()
    {
        var results = await _repo.LoadChatHistoryWithIdAsync(limit: 10);
        Assert.Empty(results);
    }
}
