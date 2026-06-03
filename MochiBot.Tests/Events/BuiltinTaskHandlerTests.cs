using System.Text.Json;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;

namespace MochiBot.Tests;

public class BuiltinTaskHandlerTests : IDisposable
{
    private readonly EventDispatcher _dispatcher;
    private readonly BuiltinTaskHandler _handler;

    public BuiltinTaskHandlerTests()
    {
        _dispatcher = new EventDispatcher();
        _handler = new BuiltinTaskHandler(_dispatcher);
    }

    public void Dispose()
    {
        _handler.Dispose();
        _dispatcher.Dispose();
    }

    // ========== 深夜关怀 ==========

    [Fact]
    public void LateNight_ShouldNotThrow()
    {
        _dispatcher.Publish(new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = JsonSerializer.Serialize(new { type = "late_night", name = "深夜关怀", parameters = "0,0" })
        });

        Assert.NotNull(_handler);
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
