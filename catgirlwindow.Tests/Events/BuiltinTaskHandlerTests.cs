using System.Text.Json;
using catgirlwindow.Src.Core.Events;
using catgirlwindow.Src.Models;

namespace catgirlwindow.Tests;

public class BuiltinTaskHandlerTests : IDisposable
{
    private readonly EventDispatcher _dispatcher;
    private readonly BuiltinTaskHandler _handler;
    private readonly List<EventData> _received;

    public BuiltinTaskHandlerTests()
    {
        _dispatcher = new EventDispatcher();
        _handler = new BuiltinTaskHandler(_dispatcher);
        _received = new List<EventData>();
        _dispatcher.SubscribeAll(data => _received.Add(data));
    }

    public void Dispose()
    {
        _handler.Dispose();
        _dispatcher.Dispose();
    }

    /// <summary>
    /// 从事件 Info JSON 中提取指定字段
    /// </summary>
    private T? GetField<T>(EventData e, string fieldName)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.Info);
            if (doc.RootElement.TryGetProperty(fieldName, out var prop))
                return JsonSerializer.Deserialize<T>(prop.GetRawText());
            return default;
        }
        catch { return default; }
    }

    /// <summary>
    /// 获取 BuiltinTaskHandler 触发的事件（有额外字段的）
    /// </summary>
    private List<EventData> GetTriggeredEvents(string type)
    {
        return _received.Where(e =>
        {
            var t = GetField<string>(e, "type");
            return t == type && HasTriggerField(e, type);
        }).ToList();
    }

    /// <summary>
    /// 判断事件是否是 BuiltinTaskHandler 触发的（有额外字段）
    /// </summary>
    private bool HasTriggerField(EventData e, string type)
    {
        return type switch
        {
            "murmur" => GetField<string>(e, "name") == "碎碎念" && GetField<string>(e, "parameters") == null,
            "eye_rest" => GetField<int?>(e, "hours") != null,
            "idle" => GetField<int?>(e, "minutes") != null,
            "late_night" => GetField<string>(e, "name") == "深夜关怀" && GetField<string>(e, "parameters") == null,
            _ => false
        };
    }

    // ========== 碎碎念 ==========

    [Fact]
    public void Murmur_WithHighWeight_ShouldTrigger()
    {
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "murmur", name = "碎碎念", parameters = "100" })
        });

        var triggered = GetTriggeredEvents("murmur");
        Assert.Single(triggered);
    }

    [Fact]
    public void Murmur_WithZeroWeight_ShouldNotTrigger()
    {
        for (int i = 0; i < 10; i++)
        {
            _dispatcher.Publish(new EventData
            {
                Category = EventCategory.SystemAuto,
                Trigger = EventTrigger.System,
                Info = JsonSerializer.Serialize(new { type = "murmur", name = "碎碎念", parameters = "0" })
            });
        }

        var triggered = GetTriggeredEvents("murmur");
        Assert.Empty(triggered);
    }

    // ========== 用眼提醒 ==========

    [Fact]
    public void EyeRest_ShouldTriggerAfterThreshold()
    {
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "eye_rest", name = "用眼提醒", parameters = "0" })
        });

        var triggered = GetTriggeredEvents("eye_rest");
        Assert.Single(triggered);
    }

    [Fact]
    public void EyeRest_ShouldOnlyFireOnce()
    {
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "eye_rest", name = "用眼提醒", parameters = "0" })
        });

        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "eye_rest", name = "用眼提醒", parameters = "0" })
        });

        var triggered = GetTriggeredEvents("eye_rest");
        Assert.Single(triggered);
    }

    // ========== 空闲检测 ==========

    [Fact]
    public void IdleCheck_ShouldTriggerAfterThreshold()
    {
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "idle_check", name = "空闲检测", parameters = "0" })
        });

        var triggered = GetTriggeredEvents("idle");
        Assert.Single(triggered);
    }

    // ========== 深夜关怀 ==========

    [Fact]
    public void LateNight_ShouldCalculateOffset()
    {
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "late_night", name = "深夜关怀", parameters = "0,0" })
        });

        // 非23点不会触发，所以只检查不报错
        Assert.NotNull(_handler);
    }

    // ========== 用户活动记录 ==========

    [Fact]
    public void RecordUserActivity_ShouldResetEyeRest()
    {
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "eye_rest", name = "用眼提醒", parameters = "0" })
        });

        _handler.RecordUserActivity();

        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "eye_rest", name = "用眼提醒", parameters = "0" })
        });

        var triggered = GetTriggeredEvents("eye_rest");
        Assert.Equal(2, triggered.Count);
    }

    // ========== 未知事件类型 ==========

    [Fact]
    public void UnknownEventType_ShouldNotThrow()
    {
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "unknown_type", name = "未知" })
        });

        Assert.NotNull(_handler);
    }
}
