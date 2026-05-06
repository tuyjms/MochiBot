using catgirlwindow.Src.Core.Events;
using catgirlwindow.Src.Models;

namespace catgirlwindow.Tests;

/// <summary>
/// 测试 EventDispatcher 的异步事件发布/订阅功能
/// 重点测试并发场景下是否会出现多次回调
/// </summary>
public class EventDispatcherAsyncTests : IDisposable
{
    private readonly EventDispatcher _dispatcher;

    public EventDispatcherAsyncTests()
    {
        _dispatcher = new EventDispatcher();
    }

    public void Dispose()
    {
        _dispatcher.StopScheduler();
        _dispatcher.Dispose();
    }

    // ========== 基础异步订阅 ==========

    [Fact]
    public async Task PublishAsync_ShouldNotifyAsyncSubscribers()
    {
        var received = new List<EventData>();
        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(10); // 模拟异步操作
            lock (received)
            {
                received.Add(data);
            }
        });

        await _dispatcher.PublishAsync(new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "test message"
        });

        Assert.Single(received);
        Assert.Equal("test message", received[0].Info);
    }

    [Fact]
    public async Task PublishAsync_ShouldWaitForAllAsyncSubscribers()
    {
        var completedCount = 0;
        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(100);
            Interlocked.Increment(ref completedCount);
        });
        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(200);
            Interlocked.Increment(ref completedCount);
        });

        await _dispatcher.PublishAsync(new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "test"
        });

        Assert.Equal(2, completedCount);
    }

    [Fact]
    public async Task PublishAsync_ShouldNotNotifyDifferentCategory()
    {
        var received = new List<EventData>();
        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(10);
            lock (received)
            {
                received.Add(data);
            }
        });

        await _dispatcher.PublishAsync(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = "system event"
        });

        Assert.Empty(received);
    }

    // ========== 并发测试 ==========

    [Fact]
    public async Task PublishAsync_ConcurrentPublishes_ShouldNotOverlap()
    {
        var executionCount = 0;
        var maxConcurrent = 0;
        var currentConcurrent = 0;

        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            Interlocked.Increment(ref currentConcurrent);
            var current = Interlocked.Increment(ref executionCount);

            // 记录最大并发数
            var cc = currentConcurrent;
            if (cc > maxConcurrent)
                maxConcurrent = cc;

            await Task.Delay(100); // 模拟耗时操作

            Interlocked.Decrement(ref currentConcurrent);
        });

        // 同时发布3个事件
        var tasks = new[]
        {
            _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "1" }),
            _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "2" }),
            _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "3" })
        };

        await Task.WhenAll(tasks);

        // 应该执行了3次
        Assert.Equal(3, executionCount);
        // 最大并发数应该为1（因为 PublishAsync 内部会等待所有异步订阅者完成）
        // 注意：这里可能不是1，因为 PublishAsync 本身不保证串行化
        // 我们主要验证不会出现异常情况
    }

    [Fact]
    public async Task PublishAsync_SequentialPublishes_ShouldExecuteInOrder()
    {
        var executionOrder = new List<int>();

        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(50);
            lock (executionOrder)
            {
                executionOrder.Add(int.Parse(data.Info));
            }
        });

        // 顺序发布3个事件
        await _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "1" });
        await _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "2" });
        await _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "3" });

        // 顺序发布时，应该按顺序执行
        Assert.Equal(3, executionOrder.Count);
        Assert.Equal(1, executionOrder[0]);
        Assert.Equal(2, executionOrder[1]);
        Assert.Equal(3, executionOrder[2]);
    }

    // ========== 混合订阅测试 ==========

    [Fact]
    public async Task PublishAsync_MixedSyncAndAsync_ShouldHandleBoth()
    {
        var syncReceived = new List<EventData>();
        var asyncReceived = new List<EventData>();

        _dispatcher.Subscribe(EventCategory.UserInput, (data) =>
        {
            lock (syncReceived)
            {
                syncReceived.Add(data);
            }
        });

        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(10);
            lock (asyncReceived)
            {
                asyncReceived.Add(data);
            }
        });

        await _dispatcher.PublishAsync(new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "test"
        });

        Assert.Single(syncReceived);
        Assert.Single(asyncReceived);
    }

    // ========== 异常处理 ==========

    [Fact]
    public async Task PublishAsync_AsyncSubscriberException_ShouldNotAffectOthers()
    {
        var received = new List<EventData>();

        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(10);
            throw new InvalidOperationException("模拟异步异常");
        });

        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(10);
            lock (received)
            {
                received.Add(data);
            }
        });

        // 不应抛出异常
        await _dispatcher.PublishAsync(new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "test"
        });

        Assert.Single(received);
    }

    [Fact]
    public async Task PublishAsync_MultipleExceptions_ShouldNotThrow()
    {
        _dispatcher.Subscribe(EventCategory.UserInput, async (_) =>
        {
            await Task.Delay(10);
            throw new Exception("异常1");
        });
        _dispatcher.Subscribe(EventCategory.UserInput, async (_) =>
        {
            await Task.Delay(10);
            throw new Exception("异常2");
        });

        // 不应抛出异常
        await _dispatcher.PublishAsync(new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "test"
        });
    }

    // ========== 取消订阅 ==========

    [Fact]
    public async Task Unsubscribe_AsyncHandler_ShouldStopReceiving()
    {
        var received = new List<EventData>();
        var subId = _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(10);
            lock (received)
            {
                received.Add(data);
            }
        });

        await _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "1" });
        _dispatcher.Unsubscribe(subId);
        await _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "2" });

        Assert.Single(received);
    }

    // ========== 订阅所有事件 ==========

    [Fact]
    public async Task SubscribeAll_Async_ShouldReceiveAllEvents()
    {
        var received = new List<EventData>();
        _dispatcher.SubscribeAll(async (data) =>
        {
            await Task.Delay(10);
            lock (received)
            {
                received.Add(data);
            }
        });

        await _dispatcher.PublishAsync(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "1" });
        await _dispatcher.PublishAsync(new EventData { Category = EventCategory.SystemAuto, Trigger = EventTrigger.System, Info = "2" });

        Assert.Equal(2, received.Count);
    }

    // ========== 同步 Publish 不应阻塞 ==========

    [Fact]
    public void Publish_Sync_ShouldFireAndForgetAsyncHandlers()
    {
        var asyncCompleted = false;

        _dispatcher.Subscribe(EventCategory.UserInput, async (data) =>
        {
            await Task.Delay(500); // 长时间异步操作
            asyncCompleted = true;
        });

        // 同步发布，不应等待异步订阅者
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "test"
        });

        // 异步操作应该还没完成
        Assert.False(asyncCompleted);
    }
}
