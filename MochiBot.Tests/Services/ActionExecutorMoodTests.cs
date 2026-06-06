using MochiBot.Src.Agent;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;

namespace MochiBot.Tests.Services;

public class ActionExecutorMoodTests
{
    private readonly List<AgentMood> _moodChanges = new();
    private readonly List<(string tag, string text)> _memoryLogs = new();
    private readonly List<string> _animations = new();

    private ActionExecutor CreateExecutor()
    {
        return new ActionExecutor(
            new NoOpToolService(),
            mood => _moodChanges.Add(mood),
            (tag, text) => _memoryLogs.Add((tag, text)),
            anim => _animations.Add(anim));
    }

    // ========== 🔴-3: HandleMoodChange 字段 fallback ==========

    [Fact]
    public async Task HandleMoodChange_MoodField_ParsesCorrectly()
    {
        var executor = CreateExecutor();
        var actions = new List<AgentAction>
        {
            new() { Type = "mood_change", Mood = "Happy" }
        };

        await executor.ExecuteActionsAsync(actions);

        Assert.Single(_moodChanges);
        Assert.Equal(AgentMood.Happy, _moodChanges[0]);
    }

    [Fact]
    public async Task HandleMoodChange_NameField_FallbackParsesCorrectly()
    {
        // 🔴 核心修复场景：LLM 将情绪放在 Name 而非 Mood
        var executor = CreateExecutor();
        var actions = new List<AgentAction>
        {
            new() { Type = "mood_change", Name = "Sad" }
        };

        await executor.ExecuteActionsAsync(actions);

        Assert.Single(_moodChanges);
        Assert.Equal(AgentMood.Sad, _moodChanges[0]);
    }

    [Fact]
    public async Task HandleMoodChange_BothFields_MoodTakesPrecedence()
    {
        // 同时有 Mood 和 Name 时，优先使用 Mood
        var executor = CreateExecutor();
        var actions = new List<AgentAction>
        {
            new() { Type = "mood_change", Mood = "Angry", Name = "Happy" }
        };

        await executor.ExecuteActionsAsync(actions);

        Assert.Single(_moodChanges);
        Assert.Equal(AgentMood.Angry, _moodChanges[0]);
    }

    [Fact]
    public async Task HandleMoodChange_NeitherField_NoMoodChange()
    {
        var executor = CreateExecutor();
        var actions = new List<AgentAction>
        {
            new() { Type = "mood_change" } // Mood 和 Name 都为 null
        };

        await executor.ExecuteActionsAsync(actions);

        Assert.Empty(_moodChanges);
    }

    [Fact]
    public async Task HandleMoodChange_InvalidValue_NoMoodChange()
    {
        var executor = CreateExecutor();
        var actions = new List<AgentAction>
        {
            new() { Type = "mood_change", Mood = "NotARealMood" }
        };

        await executor.ExecuteActionsAsync(actions);

        Assert.Empty(_moodChanges);
    }

    [Fact]
    public async Task HandleMoodChange_CaseInsensitive_ParsesCorrectly()
    {
        // Enum.TryParse with ignoreCase: true
        var executor = CreateExecutor();
        var actions = new List<AgentAction>
        {
            new() { Type = "mood_change", Mood = "happy" } // 小写
        };

        await executor.ExecuteActionsAsync(actions);

        Assert.Single(_moodChanges);
        Assert.Equal(AgentMood.Happy, _moodChanges[0]);
    }

    [Fact]
    public async Task HandleMoodChange_AllEnumValues_AllParsed()
    {
        // 验证所有枚举值都能被正确解析
        foreach (var moodName in Enum.GetNames<AgentMood>())
        {
            _moodChanges.Clear();
            var executor = CreateExecutor();
            var actions = new List<AgentAction>
            {
                new() { Type = "mood_change", Mood = moodName }
            };

            await executor.ExecuteActionsAsync(actions);

            Assert.Single(_moodChanges);
            Assert.Equal(Enum.Parse<AgentMood>(moodName), _moodChanges[0]);
        }
    }

    // ========== ActionExecutor 其他边界 ==========

    [Fact]
    public async Task ExecuteActionsAsync_NullActions_ReturnsEmpty()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteActionsAsync(null);

        Assert.Equal(string.Empty, result);
        Assert.Empty(_moodChanges);
    }

    [Fact]
    public async Task ExecuteActionsAsync_EmptyActions_ReturnsEmpty()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteActionsAsync(new List<AgentAction>());

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task ExecuteActionsAsync_MaxActions_LimitsExecution()
    {
        var executor = CreateExecutor();
        var actions = new List<AgentAction>
        {
            new() { Type = "mood_change", Mood = "Happy" },
            new() { Type = "mood_change", Mood = "Sad" },
            new() { Type = "mood_change", Mood = "Angry" },
        };

        await executor.ExecuteActionsAsync(actions, maxActions: 2);

        Assert.Equal(2, _moodChanges.Count);
    }

    // ========== 测试用 No-Op ==========

    private class NoOpToolService : IToolService
    {
        public Task<ToolResult> ExecuteToolAsync(string toolName, string parameters)
            => Task.FromResult(new ToolResult { Success = true, Data = "{}" });
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
