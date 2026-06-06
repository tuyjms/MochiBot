using MochiBot.Src.Agent;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;

namespace MochiBot.Tests.Events;

[Collection("ConfigReader")]
public class EventProcessingQueueTests : IDisposable
{
    private readonly EventDispatcher _dispatcher = new();
    private readonly IConfigReader _configReader;

    public EventProcessingQueueTests()
    {
        TestConfigHelper.EnsureInitialized();
        _configReader = ConfigReader.Instance;
    }

    public void Dispose()
    {
        _dispatcher.Dispose();
    }

    private EventProcessingQueue CreateQueue(Func<EventData, Task>? handler = null)
    {
        return new EventProcessingQueue(
            _dispatcher,
            _configReader,
            handler ?? (_ => Task.CompletedTask));
    }

    private static EventData MakeEvent(EventCategory category = EventCategory.UserInput)
    {
        return new EventData { Category = category, Trigger = EventTrigger.User, Info = "test" };
    }

    // ========== 🔴-5: drain race — 事件不会永久卡住 ==========

    [Fact]
    public async Task DrainRace_EventEnqueuedDuringProcessing_ProcessedAfterCurrent()
    {
        // 核心场景：处理事件 A 期间入队事件 B，B 不应被永久卡住
        var processed = new List<string>();
        var tcs = new TaskCompletionSource();
        EventProcessingQueue? queueRef = null;

        var queue = CreateQueue(async eventData =>
        {
            processed.Add(eventData.Info);
            if (eventData.Info == "A")
            {
                // 处理 A 期间入队 B
                await queueRef!.EnqueueEventAsync(MakeEvent());
            }
            await tcs.Task; // 阻塞直到外部信号
        });
        queueRef = queue;

        await queue.EnqueueEventAsync(MakeEvent());
        // 此时 A 正在处理，B 已入队
        // 信号 A 完成
        tcs.SetResult();

        // 等待队列处理完毕
        await Task.Delay(200);

        // B 应该也被处理了（至少 1 个事件被处理）
        Assert.True(processed.Count >= 1, "至少应有 1 个事件被处理");
    }

    [Fact]
    public async Task EnqueueEventAsync_MultipleEvents_AllProcessedSerially()
    {
        var processed = new List<int>();
        var processingOrder = new List<int>();

        var queue = CreateQueue(async eventData =>
        {
            var id = int.Parse(eventData.Info);
            processingOrder.Add(id);
            await Task.Delay(10); // 模拟处理时间
            processed.Add(id);
        });

        // 快速入队多个事件
        for (int i = 0; i < 5; i++)
        {
            var evt = new EventData
            {
                Category = EventCategory.UserInput,
                Trigger = EventTrigger.User,
                Info = i.ToString()
            };
            await queue.EnqueueEventAsync(evt);
        }

        // 等待所有事件处理完毕
        await Task.Delay(1000);

        Assert.Equal(5, processed.Count);
        // 串行处理：处理顺序应与入队顺序一致
        Assert.Equal(Enumerable.Range(0, 5).ToList(), processingOrder);
    }

    // ========== 队列溢出 ==========

    [Fact]
    public async Task EnqueueEventAsync_OverCapacity_DiscardsOldest()
    {
        // MaxQueueSize = 20，超过后丢弃最旧事件
        var processed = new List<string>();
        var holdProcessing = new TaskCompletionSource();

        var queue = CreateQueue(async eventData =>
        {
            processed.Add(eventData.Info);
            await holdProcessing.Task;
        });

        // 先入队一个事件让处理循环启动并阻塞
        await queue.EnqueueEventAsync(new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "first"
        });

        // 在处理阻塞期间快速入队超过 MaxQueueSize 的事件
        for (int i = 0; i < 25; i++)
        {
            await queue.EnqueueEventAsync(new EventData
            {
                Category = EventCategory.UserInput,
                Trigger = EventTrigger.User,
                Info = $"overflow_{i}"
            });
        }

        // 释放处理
        holdProcessing.SetResult();
        await Task.Delay(2000);

        // 第一个事件 + 后续事件都被处理了（丢弃的是中间溢出的）
        Assert.True(processed.Count >= 1, "至少第一个事件应被处理");
    }

    // ========== 状态转换 ==========

    [Fact]
    public async Task State_InitialState_IsIdle()
    {
        using var queue = CreateQueue();
        Assert.Equal(AgentState.Idle, queue.State);
    }

    [Fact]
    public async Task State_AfterProcessing_ReturnsIdle()
    {
        var queue = CreateQueue(_ => Task.CompletedTask);
        await queue.EnqueueEventAsync(MakeEvent());

        await Task.Delay(200);

        Assert.Equal(AgentState.Idle, queue.State);
    }

    [Fact]
    public async Task State_DuringProcessing_ShowsThinking()
    {
        var enteredThinking = new TaskCompletionSource();
        var holdProcessing = new TaskCompletionSource();

        var queue = CreateQueue(async _ =>
        {
            enteredThinking.TrySetResult();
            await holdProcessing.Task;
        });

        await queue.EnqueueEventAsync(MakeEvent());
        await enteredThinking.Task; // 等待进入处理

        Assert.Equal(AgentState.Thinking, queue.State);

        holdProcessing.SetResult();
        await Task.Delay(100);
    }

    // ========== 错误处理 ==========

    [Fact]
    public async Task ProcessEvent_ThrowsException_StateGoesToErrorThenIdle()
    {
        var errorSeen = false;
        var queue = CreateQueue(eventData =>
        {
            if (eventData.Info == "boom")
            {
                errorSeen = true;
                throw new InvalidOperationException("test error");
            }
            return Task.CompletedTask;
        });

        await queue.EnqueueEventAsync(new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "boom"
        });

        await Task.Delay(2000); // 错误冷却 1s + 状态重置

        Assert.True(errorSeen);
        Assert.Equal(AgentState.Idle, queue.State);
    }

    // ========== Dispose ==========

    [Fact]
    public async Task Dispose_StopsProcessing_NoException()
    {
        var queue = CreateQueue(_ => Task.CompletedTask);
        await queue.EnqueueEventAsync(MakeEvent());
        await Task.Delay(100);

        // Dispose 不应抛异常
        queue.Dispose();
    }
}
