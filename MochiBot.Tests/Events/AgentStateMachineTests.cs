using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;

namespace MochiBot.Tests.Events;

public class AgentStateMachineTests : IDisposable
{
    private readonly EventDispatcher _dispatcher = new();

    public void Dispose()
    {
        _dispatcher.Dispose();
    }

    // ========== EventDispatcher 模块状态管理 ==========

    [Fact]
    public void RegisterModule_ShouldStoreInitialState()
    {
        _dispatcher.RegisterModule("agent", "idle");

        Assert.Equal("idle", _dispatcher.GetModuleState("agent"));
    }

    [Fact]
    public void UpdateModuleState_ShouldOverwriteState()
    {
        _dispatcher.RegisterModule("agent", "idle");
        _dispatcher.UpdateModuleState("agent", "thinking");

        Assert.Equal("thinking", _dispatcher.GetModuleState("agent"));
    }

    [Fact]
    public void GetModuleState_UnknownModule_ShouldReturnUnknown()
    {
        Assert.Equal("unknown", _dispatcher.GetModuleState("nonexistent"));
    }

    [Fact]
    public void GetAllModuleStates_ShouldReturnAllRegistered()
    {
        _dispatcher.RegisterModule("agent", "idle");
        _dispatcher.RegisterModule("renderer", "busy");

        var states = _dispatcher.GetAllModuleStates();

        Assert.Equal(2, states.Count);
        Assert.Equal("idle", states["agent"]);
        Assert.Equal("busy", states["renderer"]);
    }

    [Fact]
    public void RegisterModule_SameIdTwice_ShouldOverwrite()
    {
        _dispatcher.RegisterModule("agent", "idle");
        _dispatcher.RegisterModule("agent", "thinking");

        Assert.Equal("thinking", _dispatcher.GetModuleState("agent"));
    }
}
