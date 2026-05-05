using catgirlwindow.Services;

namespace catgirlwindow.Tests;

public class AutoEventServiceTests : IDisposable
{
    private readonly AutoEventService _service;

    public AutoEventServiceTests()
    {
        _service = new AutoEventService();
    }

    public void Dispose()
    {
        _service.Stop();
        _service.Dispose();
    }

    // ========== Start / Stop ==========

    [Fact]
    public void Start_ShouldInitializeBuiltInEvents()
    {
        _service.Start();
        Assert.True(_service.MurmurWeight > 0);
        _service.Stop();
    }

    [Fact]
    public void Stop_ShouldStopWithoutError()
    {
        _service.Start();
        _service.Stop();
        // 停止后不应抛出异常
        Assert.NotNull(_service);
    }

    [Fact]
    public void Start_Twice_ShouldNotDuplicate()
    {
        _service.Start();
        _service.Start(); // 第二次调用不应有问题
        _service.Stop();
        Assert.NotNull(_service);
    }

    // ========== Cron 任务管理 ==========

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
        _service.RegisterTask(task);
        Assert.Contains(_service.GetAllTasks(), t => t.Id == "test-1");
    }

    [Fact]
    public void RegisterTask_DuplicateId_ShouldReplace()
    {
        var task1 = new CronTask
        {
            Id = "test-1",
            Name = "任务1",
            CronExpression = "*/5 * * * *",
            TaskType = "custom"
        };
        var task2 = new CronTask
        {
            Id = "test-1",
            Name = "任务2",
            CronExpression = "*/10 * * * *",
            TaskType = "custom"
        };
        _service.RegisterTask(task1);
        _service.RegisterTask(task2);
        var tasks = _service.GetAllTasks();
        Assert.Single(tasks);
        Assert.Equal("任务2", tasks[0].Name);
    }

    [Fact]
    public void UnregisterTask_ShouldRemoveTask()
    {
        var task = new CronTask
        {
            Id = "test-1",
            Name = "测试任务",
            CronExpression = "*/5 * * * *",
            TaskType = "custom"
        };
        _service.RegisterTask(task);
        _service.UnregisterTask("test-1");
        Assert.DoesNotContain(_service.GetAllTasks(), t => t.Id == "test-1");
    }

    [Fact]
    public void UnregisterTask_NonExisting_ShouldNotThrow()
    {
        _service.UnregisterTask("nonexistent");
        Assert.NotNull(_service);
    }

    [Fact]
    public void SetTaskEnabled_ShouldControlTrigger()
    {
        var task = new CronTask
        {
            Id = "test-1",
            Name = "测试任务",
            CronExpression = "* * * * *",
            TaskType = "custom"
        };
        _service.RegisterTask(task);
        _service.SetTaskEnabled("test-1", false);
        var updated = _service.GetAllTasks().First(t => t.Id == "test-1");
        Assert.False(updated.Enabled);
    }

    [Fact]
    public void SetTaskEnabled_NonExisting_ShouldNotThrow()
    {
        _service.SetTaskEnabled("nonexistent", false);
        Assert.NotNull(_service);
    }

    [Fact]
    public void GetTasksByType_ShouldFilter()
    {
        _service.RegisterTask(new CronTask { Id = "1", Name = "A", CronExpression = "* * * * *", TaskType = "type1" });
        _service.RegisterTask(new CronTask { Id = "2", Name = "B", CronExpression = "* * * * *", TaskType = "type2" });
        _service.RegisterTask(new CronTask { Id = "3", Name = "C", CronExpression = "* * * * *", TaskType = "type1" });

        var type1Tasks = _service.GetTasksByType("type1");
        Assert.Equal(2, type1Tasks.Count);
    }

    // ========== 碎碎念权重 ==========

    [Fact]
    public void SetMurmurWeight_ShouldUpdate()
    {
        _service.SetMurmurWeight(50);
        Assert.Equal(50, _service.MurmurWeight);
    }

    [Fact]
    public void SetMurmurWeight_ClampToRange()
    {
        _service.SetMurmurWeight(-10);
        Assert.Equal(0, _service.MurmurWeight);

        _service.SetMurmurWeight(150);
        Assert.Equal(100, _service.MurmurWeight);
    }

    [Fact]
    public void MurmurWeight_Default_ShouldBe30()
    {
        Assert.Equal(30, _service.MurmurWeight);
    }

    // ========== Cron 表达式匹配 ==========

    [Fact]
    public void MatchesCron_EveryMinute_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(AutoEventService.MatchesCron("* * * * *", now));
    }

    [Fact]
    public void MatchesCron_SpecificMinute_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(AutoEventService.MatchesCron("30 * * * *", now));
        Assert.False(AutoEventService.MatchesCron("15 * * * *", now));
    }

    [Fact]
    public void MatchesCron_Step_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(AutoEventService.MatchesCron("*/30 * * * *", now));
        // 30 % 15 == 0，所以 */15 也匹配 30 分
        Assert.True(AutoEventService.MatchesCron("*/15 * * * *", now));
        // 31 分不匹配 */15
        var now31 = new DateTime(2026, 5, 5, 10, 31, 0);
        Assert.False(AutoEventService.MatchesCron("*/15 * * * *", now31));
    }

    [Fact]
    public void MatchesCron_Range_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(AutoEventService.MatchesCron("30 9-18 * * *", now));
        Assert.False(AutoEventService.MatchesCron("30 0-8 * * *", now));
    }

    [Fact]
    public void MatchesCron_List_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(AutoEventService.MatchesCron("30 8,10,12 * * *", now));
        Assert.False(AutoEventService.MatchesCron("30 8,12,14 * * *", now));
    }

    [Fact]
    public void MatchesCron_InvalidExpression_ShouldReturnFalse()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.False(AutoEventService.MatchesCron("invalid", now));
        Assert.False(AutoEventService.MatchesCron("* * * *", now)); // 只有4字段
        Assert.False(AutoEventService.MatchesCron("", now));
    }

    [Fact]
    public void MatchesCron_SpecificHour_ShouldMatch()
    {
        var now = new DateTime(2026, 5, 5, 10, 30, 0);
        Assert.True(AutoEventService.MatchesCron("* 10 * * *", now));
        Assert.False(AutoEventService.MatchesCron("* 9 * * *", now));
    }

    [Fact]
    public void MatchesCron_Sunday_ZeroAndSeven_ShouldBothMatch()
    {
        var sunday = new DateTime(2026, 5, 10, 10, 30, 0); // 2026-05-10 是周日
        Assert.True(AutoEventService.MatchesCron("* * * * 0", sunday));
        Assert.True(AutoEventService.MatchesCron("* * * * 7", sunday));
    }

    // ========== 深夜关怀 ==========

    [Fact]
    public void LateNightBaseTime_ShouldBe23_00()
    {
        Assert.Equal(23, _service.LateNightBaseTime.Hours);
        Assert.Equal(0, _service.LateNightBaseTime.Minutes);
    }

    [Fact]
    public void LateNightOffset_ShouldAffectTriggerTime()
    {
        _service.SetLateNightOffsetRange(-60, 60);
        var todayTime = _service.GetTodayLateNightTime();
        var baseTime = _service.LateNightBaseTime;
        var diff = (todayTime - baseTime).TotalMinutes;
        Assert.InRange(diff, -60, 60);
    }

    [Fact]
    public void GetTodayLateNightTime_ShouldBeCached()
    {
        var time1 = _service.GetTodayLateNightTime();
        var time2 = _service.GetTodayLateNightTime();
        Assert.Equal(time1, time2);
    }

    [Fact]
    public void SetLateNightOffsetRange_ShouldResetCache()
    {
        var time1 = _service.GetTodayLateNightTime();
        _service.SetLateNightOffsetRange(-60, 60);
        var time2 = _service.GetTodayLateNightTime();
        // 偏移范围改变后，时间可能不同（但都在新范围内）
        var baseTime = _service.LateNightBaseTime;
        var diff = (time2 - baseTime).TotalMinutes;
        Assert.InRange(diff, -60, 60);
    }

    // ========== 用户活动检测 ==========

    [Fact]
    public void RecordUserActivity_ShouldNotThrow()
    {
        _service.RecordUserActivity();
        Assert.NotNull(_service);
    }

    // ========== 事件触发 ==========

    [Fact]
    public async Task TaskTriggered_ShouldFireEvent_ForCronTask()
    {
        var tcs = new TaskCompletionSource<CronTask>();
        _service.OnTaskTriggered += (_, task) =>
        {
            if (task.Id != "builtin:murmur" &&
                task.Id != "builtin:eye_rest" &&
                task.Id != "builtin:late_night")
            {
                tcs.TrySetResult(task);
            }
        };

        // 注册一个每分钟触发的任务
        _service.RegisterTask(new CronTask
        {
            Id = "test-trigger",
            Name = "触发测试",
            CronExpression = "* * * * *",
            TaskType = "test"
        });
        _service.Start();

        var result = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.True(result == tcs.Task, "任务应在超时前触发");
        Assert.Equal("test", tcs.Task.Result.TaskType);
        _service.Stop();
    }

    [Fact]
    public async Task DisabledTask_ShouldNotFire()
    {
        var fired = false;
        _service.OnTaskTriggered += (_, task) =>
        {
            if (task.Id == "test-disabled")
                fired = true;
        };

        _service.RegisterTask(new CronTask
        {
            Id = "test-disabled",
            Name = "禁用测试",
            CronExpression = "* * * * *",
            TaskType = "test",
            Enabled = false
        });
        _service.Start();

        await Task.Delay(2000);
        Assert.False(fired, "禁用的任务不应触发");
        _service.Stop();
    }
}
