using MochiBot.Src.Agent;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using static MochiBot.Src.Core.Constants;

namespace MochiBot.Tests.Services;

public class AutoEventFilterTests
{
    private readonly FakeToolService _toolService = new();
    private readonly List<(string role, string text)> _memoryLogs = new();
    private readonly List<EventData> _publishedEvents = new();

    private AutoEventFilter CreateFilter()
    {
        return new AutoEventFilter(
            _toolService,
            (role, text) => _memoryLogs.Add((role, text)),
            evt => _publishedEvents.Add(evt));
    }

    private static EventData MakeSystemAuto(string type, string? parameters = null)
    {
        var info = parameters != null
            ? $"{{\"type\":\"{type}\",\"parameters\":\"{parameters}\"}}"
            : $"{{\"type\":\"{type}\"}}";
        return new EventData { Category = EventCategory.SystemAuto, Trigger = EventTrigger.System, Info = info };
    }

    private static EventData MakeUserInput(string text = "hello")
    {
        return new EventData { Category = EventCategory.UserInput, Trigger = EventTrigger.User, Info = text };
    }

    // ========== 🔴-1: async 不再阻塞线程 ==========

    [Fact]
    public async Task UpdateAsync_MurmurEvent_AwaitsToolServiceProperly()
    {
        // 验证：UpdateAsync 正确 await ExecuteToolAsync（不再 .GetAwaiter().GetResult()）
        // 如果是同步阻塞，_toolService.WasAwaited 仍为 false
        var filter = CreateFilter();
        var murmurEvent = MakeSystemAuto(BuiltinTasks.Murmur, "0"); // weight=0 → 100% 走内置文本

        _toolService.NextResult = new ToolResult
        {
            Success = true,
            Data = $"{{\"{Tools.Murmur}\":\"内置碎碎念\"}}"
        };

        // weight=0 → roll(0-99) >= 0 总是 true → 走内置文本路径
        var result = await filter.UpdateAsync(murmurEvent);

        Assert.Equal(AutoEventResult.Handled, result);
        Assert.True(_toolService.WasAwaited, "ExecuteToolAsync 应被 await 而非同步阻塞");
    }

    [Fact]
    public async Task UpdateAsync_MurmurHighWeight_ReturnsContinueForLlm()
    {
        // weight=100 → roll(0-99) < 100 总是 true → 返回 false（走 LLM）
        var filter = CreateFilter();
        var murmurEvent = MakeSystemAuto(BuiltinTasks.Murmur, "100");

        var result = await filter.UpdateAsync(murmurEvent);

        Assert.Equal(AutoEventResult.Continue, result);
        Assert.False(_toolService.WasCalled, "高权重时不应调用内置工具");
    }

    [Fact]
    public async Task UpdateAsync_MurmurToolFailure_StillHandled()
    {
        // 内置工具执行失败时仍返回 Handled（已尝试处理，不交给 LLM）
        var filter = CreateFilter();
        var murmurEvent = MakeSystemAuto(BuiltinTasks.Murmur, "0");

        _toolService.NextResult = new ToolResult { Success = false, Error = "tool error" };

        var result = await filter.UpdateAsync(murmurEvent);

        Assert.Equal(AutoEventResult.Handled, result);
    }

    [Fact]
    public async Task UpdateAsync_MurmurPublishesReplyEvent()
    {
        // 内置碎碎念成功时应发布 ToolResult 事件（带 reply 类型）
        var filter = CreateFilter();
        // weight=-1 → roll(0-99) < -1 恒 false → 确定走内置文本，消除随机性
        var murmurEvent = MakeSystemAuto(BuiltinTasks.Murmur, "-1");

        var murmurText = "hello-murmur-text"; // ASCII 避免 System.Text.Json \uXXXX 转义
        _toolService.NextResult = new ToolResult
        {
            Success = true,
            Data = $"{{\"{Tools.Murmur}\":\"{murmurText}\"}}"
        };

        await filter.UpdateAsync(murmurEvent);

        Assert.Single(_publishedEvents);
        Assert.Contains("reply", _publishedEvents[0].Info);
        Assert.Contains(murmurText, _publishedEvents[0].Info);
        Assert.Single(_memoryLogs);
        Assert.Equal(ChatRoles.Assistant, _memoryLogs[0].role);
    }

    // ========== 非 SystemAuto 事件穿透 ==========

    [Fact]
    public async Task UpdateAsync_UserInput_ReturnsContinue()
    {
        var filter = CreateFilter();
        var result = await filter.UpdateAsync(MakeUserInput());

        Assert.Equal(AutoEventResult.Continue, result);
    }

    [Fact]
    public async Task UpdateAsync_NonSystemAuto_ReturnsContinue()
    {
        var filter = CreateFilter();
        var toolResult = new EventData
        {
            Category = EventCategory.ToolResult,
            Trigger = EventTrigger.Tool,
            Info = "{}"
        };

        var result = await filter.UpdateAsync(toolResult);

        Assert.Equal(AutoEventResult.Continue, result);
    }

    // ========== ShouldProcessEvent 条件检查 ==========

    [Fact]
    public async Task UpdateAsync_EyeRest_JustActive_Skips()
    {
        // 刚有用户活动 → 用眼提醒不满足阈值 → Skip
        var filter = CreateFilter();
        await filter.UpdateAsync(MakeUserInput()); // 记录活动时间

        var eyeRestEvent = MakeSystemAuto(BuiltinTasks.EyeRest, "120"); // 阈值 120 分钟
        var result = await filter.UpdateAsync(eyeRestEvent);

        Assert.Equal(AutoEventResult.Skip, result);
    }

    [Fact]
    public async Task UpdateAsync_NonMurmurSystemAuto_ReturnsContinue()
    {
        // 非碎碎念的 SystemAuto 事件（如 EyeRest 但不检查时间）应返回 Continue
        var filter = CreateFilter();
        var genericEvent = new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = "{\"type\":\"custom_task\"}" // 非 EyeRest/IdleCheck/Murmur
        };

        var result = await filter.UpdateAsync(genericEvent);

        Assert.Equal(AutoEventResult.Continue, result);
    }

    // ========== 边界：空/畸形 Info ==========

    [Fact]
    public async Task UpdateAsync_EmptyInfo_ReturnsContinue()
    {
        var filter = CreateFilter();
        var badEvent = new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = ""
        };

        var result = await filter.UpdateAsync(badEvent);

        Assert.Equal(AutoEventResult.Continue, result);
    }

    [Fact]
    public async Task UpdateAsync_InvalidJson_ReturnsContinue()
    {
        var filter = CreateFilter();
        var badEvent = new EventData
        {
            Category = EventCategory.SystemAuto,
            Trigger = EventTrigger.System,
            Info = "not-json"
        };

        var result = await filter.UpdateAsync(badEvent);

        Assert.Equal(AutoEventResult.Continue, result);
    }

    [Fact]
    public async Task UpdateAsync_MurmurEmptyMurmurText_HandledButNoPublish()
    {
        // 工具返回空文本时不发布事件但仍返回 Handled
        var filter = CreateFilter();
        var murmurEvent = MakeSystemAuto(BuiltinTasks.Murmur, "0");

        _toolService.NextResult = new ToolResult
        {
            Success = true,
            Data = $"{{\"{Tools.Murmur}\":\"\"}}"
        };

        var result = await filter.UpdateAsync(murmurEvent);

        Assert.Equal(AutoEventResult.Handled, result);
        Assert.Empty(_publishedEvents); // 空文本不发布
        Assert.Empty(_memoryLogs);
    }

    // ========== 测试用 Fake ==========

    private class FakeToolService : IToolService
    {
        public ToolResult? NextResult { get; set; }
        public bool WasCalled { get; private set; }
        public bool WasAwaited { get; private set; }
        public string? LastToolName { get; private set; }

        public async Task<ToolResult> ExecuteToolAsync(string toolName, string parameters)
        {
            WasCalled = true;
            LastToolName = toolName;
            // 模拟异步操作（确认调用方确实 await 了）
            await Task.Yield();
            WasAwaited = true;
            return NextResult ?? new ToolResult { Success = false, Error = "No result configured" };
        }

        // 以下方法不会被 AutoEventFilter 调用，提供最小实现
        public List<ToolDefinition> GetToolDefinitions() => new();
        public string GetFormatInstruction() => "";
        public List<ToolDefinition> GetMoodBasedTools(AgentMood mood) => new();
        public Task<List<ToolDefinition>> ListPluginsAsync() => Task.FromResult(new List<ToolDefinition>());
        public Task<List<ToolDefinition>> ListMcpToolsAsync() => Task.FromResult(new List<ToolDefinition>());
        public Task LoadModsAsync(string modDirectory) => Task.CompletedTask;
        public Task StartTimerAsync(int seconds, Action onComplete) => Task.CompletedTask;
        public void StopTimer() { }
        public void TogglePauseTimer() { }
        public int GetTimerRemaining() => 0;
        public TimerStatus GetTimerStatus() => TimerStatus.Idle;
    }
}
