using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;

namespace MochiBot.Tests;

public class EventDispatcherTests : IDisposable
{
    private readonly EventDispatcher _dispatcher;

    public EventDispatcherTests()
    {
        _dispatcher = new EventDispatcher();
    }

    public void Dispose()
    {
        _dispatcher.StopScheduler();
        _dispatcher.Dispose();
    }

    // ========== 事件发布/订阅 ==========

    [Fact]
    public void Publish_ShouldNotifySubscribers()
    {
        var received = new List<EventData>();
        _dispatcher.Subscribe(EventCategory.UserInput, data => received.Add(data));

        var eventData = new EventData
        {
            Category = EventCategory.UserInput,
            Trigger = EventTrigger.User,
            Info = "test message"
        };
        _dispatcher.Publish(eventData);

        Assert.Single(received);
        Assert.Equal("test message", received[0].Info);
    }

    [Fact]
    public void Publish_ShouldNotNotifyDifferentCategory()
    {
        var received = new List<EventData>();
        _dispatcher.Subscribe(EventCategory.UserInput, data => received.Add(data));

        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = "system event"
        });

        Assert.Empty(received);
    }

    [Fact]
    public void SubscribeAll_ShouldReceiveAllEvents()
    {
        var received = new List<EventData>();
        _dispatcher.SubscribeAll(data => received.Add(data));

        _dispatcher.Publish(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "1" });
        _dispatcher.Publish(new EventData { Category = EventCategory.SystemAuto, Trigger = EventTrigger.System, Info = "2" });
        _dispatcher.Publish(new EventData { Category = EventCategory.MoodChange, Trigger = EventTrigger.System, Info = "3" });

        Assert.Equal(3, received.Count);
    }

    [Fact]
    public void Unsubscribe_ShouldStopReceiving()
    {
        var received = new List<EventData>();
        var subId = _dispatcher.Subscribe(EventCategory.UserInput, data => received.Add(data));

        _dispatcher.Publish(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "1" });
        _dispatcher.Unsubscribe(subId);
        _dispatcher.Publish(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "2" });

        Assert.Single(received);
    }

    [Fact]
    public void Unsubscribe_NonExisting_ShouldNotThrow()
    {
        _dispatcher.Unsubscribe("nonexistent");
        Assert.NotNull(_dispatcher);
    }

    [Fact]
    public void GetSubscriberCount_ShouldReturnCorrectCount()
    {
        _dispatcher.Subscribe(EventCategory.UserInput, _ => { });
        _dispatcher.Subscribe(EventCategory.UserInput, _ => { });
        _dispatcher.Subscribe(EventCategory.SystemAuto, _ => { });

        Assert.Equal(2, _dispatcher.GetSubscriberCount(EventCategory.UserInput));
        Assert.Equal(1, _dispatcher.GetSubscriberCount(EventCategory.SystemAuto));
        Assert.Equal(0, _dispatcher.GetSubscriberCount(EventCategory.MoodChange));
    }

    [Fact]
    public void Publish_SubscriberException_ShouldNotAffectOthers()
    {
        var received = new List<EventData>();
        _dispatcher.Subscribe(EventCategory.UserInput, _ => throw new InvalidOperationException("模拟异常"));
        _dispatcher.Subscribe(EventCategory.UserInput, data => received.Add(data));

        _dispatcher.Publish(new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = "test" });

        Assert.Single(received);
    }

    // ========== 定时任务管理 ==========

    [Fact]
    public void RegisterTask_ShouldAddTask()
    {
        var task = new CronTask
        {
            Id = "test-1",
            Name = "测试任务",
            CronExpression = "*/5 * * * *",
            TaskType = "custom"
        };
        _dispatcher.RegisterTask(task);
        Assert.Contains(_dispatcher.GetAllTasks(), t => t.Id == "test-1");
    }

    [Fact]
    public void RegisterTask_DuplicateId_ShouldReplace()
    {
        var task1 = new CronTask { Id = "test-1", Name = "任务1", CronExpression = "*/5 * * * *", TaskType = "custom" };
        var task2 = new CronTask { Id = "test-1", Name = "任务2", CronExpression = "*/10 * * * *", TaskType = "custom" };
        _dispatcher.RegisterTask(task1);
        _dispatcher.RegisterTask(task2);
        var tasks = _dispatcher.GetAllTasks();
        Assert.Single(tasks);
        Assert.Equal("任务2", tasks[0].Name);
    }

    [Fact]
    public void UnregisterTask_ShouldRemoveTask()
    {
        _dispatcher.RegisterTask(new CronTask { Id = "test-1", Name = "测试", CronExpression = "* * * * *", TaskType = "custom" });
        _dispatcher.UnregisterTask("test-1");
        Assert.DoesNotContain(_dispatcher.GetAllTasks(), t => t.Id == "test-1");
    }

    [Fact]
    public void UnregisterTask_NonExisting_ShouldNotThrow()
    {
        _dispatcher.UnregisterTask("nonexistent");
        Assert.NotNull(_dispatcher);
    }

    [Fact]
    public void SetTaskEnabled_ShouldControlTask()
    {
        _dispatcher.RegisterTask(new CronTask { Id = "test-1", Name = "测试", CronExpression = "* * * * *", TaskType = "custom" });
        _dispatcher.SetTaskEnabled("test-1", false);
        var task = _dispatcher.GetAllTasks().First(t => t.Id == "test-1");
        Assert.False(task.Enabled);
    }

    [Fact]
    public void SetTaskEnabled_NonExisting_ShouldNotThrow()
    {
        _dispatcher.SetTaskEnabled("nonexistent", false);
        Assert.NotNull(_dispatcher);
    }

    [Fact]
    public void RecordUserActivity_ShouldReturnCurrentTime()
    {
        var before = DateTime.Now.TimeOfDay;
        var result = _dispatcher.RecordUserActivity();
        var after = DateTime.Now.TimeOfDay;
        Assert.InRange(result, before, after);
    }

    // ========== Cron 表达式匹配 ==========

    [Fact]
    public void MatchesCron_EveryMinute_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(EventDispatcher.MatchesCron("* * * * *", now));
    }

    [Fact]
    public void MatchesCron_SpecificMinute_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(EventDispatcher.MatchesCron("30 * * * *", now));
        Assert.False(EventDispatcher.MatchesCron("15 * * * *", now));
    }

    [Fact]
    public void MatchesCron_Step_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(EventDispatcher.MatchesCron("*/30 * * * *", now));
        Assert.True(EventDispatcher.MatchesCron("*/15 * * * *", now));
        var now31 = new DateTime(2026, 5, 5, 10, 31, 0);
        Assert.False(EventDispatcher.MatchesCron("*/15 * * * *", now31));
    }

    [Fact]
    public void MatchesCron_Range_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(EventDispatcher.MatchesCron("30 9-18 * * *", now));
        Assert.False(EventDispatcher.MatchesCron("30 0-8 * * *", now));
    }

    [Fact]
    public void MatchesCron_List_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(EventDispatcher.MatchesCron("30 8,10,12 * * *", now));
        Assert.False(EventDispatcher.MatchesCron("30 8,12,14 * * *", now));
    }

    [Fact]
    public void MatchesCron_InvalidExpression_ShouldReturnFalse()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.False(EventDispatcher.MatchesCron("invalid", now));
        Assert.False(EventDispatcher.MatchesCron("* * * *", now));
        Assert.False(EventDispatcher.MatchesCron("", now));
    }

    [Fact]
    public void MatchesCron_Sunday_ZeroAndSeven_ShouldBothMatch()
    {
        var sunday = new DateTime(2026, 5, 10, 10, 30, 0);
        Assert.True(EventDispatcher.MatchesCron("* * * * 0", sunday));
        Assert.True(EventDispatcher.MatchesCron("* * * * 7", sunday));
    }

    // ========== 定时任务触发 ==========

    [Fact]
    public async Task StartScheduler_ShouldTriggerCronTask()
    {
        var tcs = new TaskCompletionSource<EventData>();
        _dispatcher.Subscribe(EventCategory.SystemAuto, data =>
        {
            tcs.TrySetResult(data);
        });

        _dispatcher.RegisterTask(new CronTask
        {
            Id = "test-trigger",
            Name = "触发测试",
            CronExpression = "* * * * *",
            TaskType = "test"
        });
        _dispatcher.StartScheduler();

        var result = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.True(result == tcs.Task, "任务应在超时前触发");
        Assert.Contains("test", tcs.Task.Result.Info); // Info 是 JSON，包含 type=test
        _dispatcher.StopScheduler();
    }

    [Fact]
    public async Task DisabledTask_ShouldNotFire()
    {
        var fired = false;
        _dispatcher.Subscribe(EventCategory.SystemAuto, data =>
        {
            fired = true;
        });

        _dispatcher.RegisterTask(new CronTask
        {
            Id = "test-disabled",
            Name = "禁用测试",
            CronExpression = "* * * * *",
            TaskType = "test",
            Enabled = false
        });
        _dispatcher.StartScheduler();

        await Task.Delay(2000);
        Assert.False(fired, "禁用的任务不应触发");
        _dispatcher.StopScheduler();
    }

    [Fact]
    public void StartScheduler_Twice_ShouldNotDuplicate()
    {
        _dispatcher.StartScheduler();
        _dispatcher.StartScheduler();
        _dispatcher.StopScheduler();
        Assert.NotNull(_dispatcher);
    }

    [Fact]
    public void StopScheduler_ShouldStopWithoutError()
    {
        _dispatcher.StartScheduler();
        _dispatcher.StopScheduler();
        Assert.NotNull(_dispatcher);
    }
}
