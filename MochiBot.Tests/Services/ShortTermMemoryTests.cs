using MochiBot.Src.Agent;
namespace MochiBot.Tests;

public class ShortTermMemoryTests
{
    // ========== 基本功能 ==========

    [Fact]
    public void AddMessage_ShouldIncreaseCount()
    {
        var memory = new ShortTermMemory(capacity: 5);
        memory.AddMessage("user", "你好");
        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void AddMessage_Multiple_ShouldIncreaseCount()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.AddMessage("user", "msg1");
        memory.AddMessage("assistant", "reply1");
        memory.AddMessage("user", "msg2");
        Assert.Equal(3, memory.Count);
    }

    [Fact]
    public void Count_Initial_ShouldBeZero()
    {
        var memory = new ShortTermMemory(capacity: 10);
        Assert.Equal(0, memory.Count);
    }

    // ========== 获取消息 ==========

    [Fact]
    public void GetRecentMessages_ShouldReturnCorrectCount()
    {
        var memory = new ShortTermMemory(capacity: 10);
        for (int i = 0; i < 5; i++)
            memory.AddMessage("user", $"msg{i}");

        var recent = memory.GetRecentMessages(3);
        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public void GetRecentMessages_DefaultCount_ShouldBe10()
    {
        var memory = new ShortTermMemory(capacity: 20);
        for (int i = 0; i < 15; i++)
            memory.AddMessage("user", $"msg{i}");

        var recent = memory.GetRecentMessages(); // 默认10条
        Assert.Equal(10, recent.Count);
    }

    [Fact]
    public void GetRecentMessages_RequestMoreThanAvailable_ShouldReturnAll()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.AddMessage("user", "msg1");
        memory.AddMessage("user", "msg2");

        var recent = memory.GetRecentMessages(10);
        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public void GetRecentMessages_ShouldReturnMostRecent()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.AddMessage("user", "first");
        memory.AddMessage("user", "second");
        memory.AddMessage("user", "third");

        var recent = memory.GetRecentMessages(2);
        Assert.Equal(2, recent.Count);
        Assert.Equal("second", recent[0].Content);
        Assert.Equal("third", recent[1].Content);
    }

    [Fact]
    public void GetRecentMessages_ZeroCount_ShouldReturnEmpty()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.AddMessage("user", "test");
        var recent = memory.GetRecentMessages(0);
        Assert.Empty(recent);
    }

    [Fact]
    public void GetAllMessages_ShouldReturnAll()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.AddMessage("user", "msg1");
        memory.AddMessage("assistant", "reply1");
        memory.AddMessage("user", "msg2");

        var all = memory.GetAllMessages();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetAllMessages_Empty_ShouldReturnEmpty()
    {
        var memory = new ShortTermMemory(capacity: 10);
        var all = memory.GetAllMessages();
        Assert.Empty(all);
    }

    // ========== 环形缓冲区溢出 ==========

    [Fact]
    public void CircularBuffer_ShouldOverwriteOldest()
    {
        var memory = new ShortTermMemory(capacity: 3);
        memory.AddMessage("user", "msg1");
        memory.AddMessage("user", "msg2");
        memory.AddMessage("user", "msg3");
        memory.AddMessage("user", "msg4"); // 溢出，覆盖 msg1

        var recent = memory.GetRecentMessages(3);
        Assert.Equal(3, recent.Count);
        Assert.Equal("msg2", recent[0].Content);
        Assert.Equal("msg3", recent[1].Content);
        Assert.Equal("msg4", recent[2].Content);
    }

    [Fact]
    public void CircularBuffer_MultipleOverwrites_ShouldWork()
    {
        var memory = new ShortTermMemory(capacity: 3);
        for (int i = 1; i <= 10; i++)
            memory.AddMessage("user", $"msg{i}");

        Assert.Equal(3, memory.Count);
        var recent = memory.GetRecentMessages(3);
        Assert.Equal("msg8", recent[0].Content);
        Assert.Equal("msg9", recent[1].Content);
        Assert.Equal("msg10", recent[2].Content);
    }

    [Fact]
    public void CircularBuffer_ExactCapacity_ShouldNotOverwrite()
    {
        var memory = new ShortTermMemory(capacity: 3);
        memory.AddMessage("user", "msg1");
        memory.AddMessage("user", "msg2");
        memory.AddMessage("user", "msg3");

        Assert.Equal(3, memory.Count);
        var all = memory.GetAllMessages();
        Assert.Equal("msg1", all[0].Content);
        Assert.Equal("msg2", all[1].Content);
        Assert.Equal("msg3", all[2].Content);
    }

    // ========== Clear ==========

    [Fact]
    public void Clear_ShouldResetCount()
    {
        var memory = new ShortTermMemory(capacity: 5);
        memory.AddMessage("user", "test");
        memory.Clear();
        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void Clear_ShouldClearAllMessages()
    {
        var memory = new ShortTermMemory(capacity: 5);
        memory.AddMessage("user", "msg1");
        memory.AddMessage("user", "msg2");
        memory.Clear();

        var all = memory.GetAllMessages();
        Assert.Empty(all);
    }

    [Fact]
    public void Clear_AfterClear_AddShouldWork()
    {
        var memory = new ShortTermMemory(capacity: 5);
        memory.AddMessage("user", "msg1");
        memory.Clear();
        memory.AddMessage("user", "new_msg");

        Assert.Equal(1, memory.Count);
        var recent = memory.GetRecentMessages(1);
        Assert.Equal("new_msg", recent[0].Content);
    }

    // ========== Capacity ==========

    [Fact]
    public void Capacity_Default_ShouldBe50()
    {
        var memory = new ShortTermMemory();
        Assert.Equal(50, memory.Capacity);
    }

    [Fact]
    public void Capacity_Custom_ShouldWork()
    {
        var memory = new ShortTermMemory(capacity: 20);
        Assert.Equal(20, memory.Capacity);
    }

    [Fact]
    public void Capacity_Zero_ShouldUseDefault()
    {
        var memory = new ShortTermMemory(capacity: 0);
        Assert.Equal(50, memory.Capacity);
    }

    [Fact]
    public void Capacity_Negative_ShouldUseDefault()
    {
        var memory = new ShortTermMemory(capacity: -5);
        Assert.Equal(50, memory.Capacity);
    }

    [Fact]
    public void SetCapacity_ShouldResize()
    {
        var memory = new ShortTermMemory(capacity: 10);
        for (int i = 1; i <= 8; i++)
            memory.AddMessage("user", $"msg{i}");

        memory.Capacity = 5;
        Assert.Equal(5, memory.Capacity);
        Assert.Equal(5, memory.Count);

        var recent = memory.GetRecentMessages(5);
        Assert.Equal("msg4", recent[0].Content);
        Assert.Equal("msg8", recent[4].Content);
    }

    [Fact]
    public void SetCapacity_Larger_ShouldKeepAll()
    {
        var memory = new ShortTermMemory(capacity: 3);
        memory.AddMessage("user", "msg1");
        memory.AddMessage("user", "msg2");
        memory.AddMessage("user", "msg3");

        memory.Capacity = 10;
        Assert.Equal(10, memory.Capacity);
        Assert.Equal(3, memory.Count);
    }

    [Fact]
    public void SetCapacity_Zero_ShouldNotChange()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.Capacity = 0;
        Assert.Equal(10, memory.Capacity);
    }

    // ========== OverflowStrategy ==========

    [Fact]
    public void OverflowStrategy_Default_ShouldBeTruncate()
    {
        var memory = new ShortTermMemory(capacity: 5);
        Assert.Equal(OverflowStrategy.Truncate, memory.OverflowStrategy);
    }

    [Fact]
    public void OverflowStrategy_Truncate_ShouldDiscardOldest()
    {
        var memory = new ShortTermMemory(capacity: 3);
        memory.OverflowStrategy = OverflowStrategy.Truncate;

        memory.AddMessage("user", "msg1");
        memory.AddMessage("user", "msg2");
        memory.AddMessage("user", "msg3");
        memory.AddMessage("user", "msg4"); // 溢出，丢弃 msg1

        Assert.Equal(3, memory.Count);
        var all = memory.GetAllMessages();
        Assert.Equal("msg2", all[0].Content);
        Assert.Equal("msg4", all[2].Content);
    }

    [Fact]
    public void OverflowStrategy_Summarize_ShouldNotLoseMessages()
    {
        var memory = new ShortTermMemory(capacity: 3);
        memory.OverflowStrategy = OverflowStrategy.Summarize;

        memory.AddMessage("user", "msg1");
        memory.AddMessage("user", "msg2");
        memory.AddMessage("user", "msg3");
        memory.AddMessage("user", "msg4"); // 溢出

        // Summarize 策略下，溢出时先按 Truncate 处理
        Assert.Equal(3, memory.Count);
    }

    // ========== SummarizeAsync ==========

    [Fact]
    public async Task SummarizeAsync_ShouldReturnSummary()
    {
        var memory = new ShortTermMemory(capacity: 20);
        memory.AddMessage("user", "你好");
        memory.AddMessage("assistant", "你好！有什么可以帮助你的吗？");
        memory.AddMessage("user", "今天天气怎么样？");

        var summary = await memory.SummarizeAsync();
        Assert.NotNull(summary);
        Assert.Contains("对话摘要", summary);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldSetContextSummary()
    {
        var memory = new ShortTermMemory(capacity: 20);
        memory.AddMessage("user", "你好");

        Assert.Null(memory.ContextSummary);
        await memory.SummarizeAsync();
        Assert.NotNull(memory.ContextSummary);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldReduceCount()
    {
        var memory = new ShortTermMemory(capacity: 20);
        for (int i = 0; i < 15; i++)
            memory.AddMessage("user", $"msg{i}");

        await memory.SummarizeAsync();
        // 摘要(1条) + 保留的最近10条 = 11条
        Assert.Equal(11, memory.Count);
    }

    [Fact]
    public async Task SummarizeAsync_Empty_ShouldWork()
    {
        var memory = new ShortTermMemory(capacity: 10);
        var summary = await memory.SummarizeAsync();
        Assert.NotNull(summary);
        Assert.Equal(1, memory.Count); // 只有摘要
    }

    // ========== 消息内容验证 ==========

    [Fact]
    public void AddMessage_ShouldStoreRoleAndContent()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.AddMessage("user", "测试消息");

        var recent = memory.GetRecentMessages(1);
        Assert.Single(recent);
        Assert.Equal("user", recent[0].Role);
        Assert.Equal("测试消息", recent[0].Content);
    }

    [Fact]
    public void AddMessage_ShouldSetTimestamp()
    {
        var memory = new ShortTermMemory(capacity: 10);
        var before = DateTime.Now.AddSeconds(-1);
        memory.AddMessage("user", "test");
        var after = DateTime.Now.AddSeconds(1);

        var recent = memory.GetRecentMessages(1);
        Assert.Single(recent);
        Assert.True(recent[0].Timestamp >= before);
        Assert.True(recent[0].Timestamp <= after);
    }

    // ========== 边界情况 ==========

    [Fact]
    public void Capacity_One_ShouldWork()
    {
        var memory = new ShortTermMemory(capacity: 1);
        memory.AddMessage("user", "msg1");
        Assert.Equal(1, memory.Count);

        memory.AddMessage("user", "msg2");
        Assert.Equal(1, memory.Count);

        var recent = memory.GetRecentMessages(1);
        Assert.Equal("msg2", recent[0].Content);
    }

    [Fact]
    public void AddMessage_EmptyContent_ShouldWork()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.AddMessage("user", "");
        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void AddMessage_EmptyRole_ShouldWork()
    {
        var memory = new ShortTermMemory(capacity: 10);
        memory.AddMessage("", "content");
        Assert.Equal(1, memory.Count);
    }
}
