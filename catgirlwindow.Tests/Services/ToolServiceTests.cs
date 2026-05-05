using catgirlwindow.Models;
using catgirlwindow.Services;

namespace catgirlwindow.Tests;

public class ToolServiceTests : IDisposable
{
    private readonly FakeLlmClient _llmClient;
    private readonly FakeMoodTracker _moodTracker;
    private readonly FakePromptFormatter _formatter;
    private readonly ToolService _service;

    public ToolServiceTests()
    {
        _llmClient = new FakeLlmClient();
        _moodTracker = new FakeMoodTracker();
        _formatter = new FakePromptFormatter();
        _service = new ToolService(_llmClient, _moodTracker, _formatter);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    // ========== 工具定义 ==========

    [Fact]
    public void GetToolDefinitions_ShouldReturnBaseTools()
    {
        var tools = _service.GetToolDefinitions();
        Assert.Contains(tools, t => t.Name == "timer");
        Assert.Contains(tools, t => t.Name == "compliment");
        Assert.Contains(tools, t => t.Name == "pet");
        Assert.Contains(tools, t => t.Name == "weather");
        Assert.Contains(tools, t => t.Name == "list_plugins");
    }

    [Fact]
    public void GetToolDefinitions_ShouldReturnCorrectCount()
    {
        var tools = _service.GetToolDefinitions();
        Assert.Equal(5, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_Timer_ShouldHaveSecondsParam()
    {
        var timer = _service.GetToolDefinitions().First(t => t.Name == "timer");
        Assert.Contains("seconds", timer.InputSchema["required"] as IEnumerable<string> ?? Array.Empty<string>());
    }

    // ========== 情绪附加工具 ==========

    [Fact]
    public void GetMoodBasedTools_Sad_ShouldIncludeHug()
    {
        var tools = _service.GetMoodBasedTools(AgentMood.Sad);
        Assert.Contains(tools, t => t.Name == "hug");
    }

    [Fact]
    public void GetMoodBasedTools_Happy_ShouldIncludeDance()
    {
        var tools = _service.GetMoodBasedTools(AgentMood.Happy);
        Assert.Contains(tools, t => t.Name == "dance");
    }

    [Fact]
    public void GetMoodBasedTools_Sleepy_ShouldIncludeTuckIn()
    {
        var tools = _service.GetMoodBasedTools(AgentMood.Sleepy);
        Assert.Contains(tools, t => t.Name == "tuck_in");
    }

    [Fact]
    public void GetMoodBasedTools_Touched_ShouldIncludeCuddle()
    {
        var tools = _service.GetMoodBasedTools(AgentMood.Touched);
        Assert.Contains(tools, t => t.Name == "cuddle");
    }

    [Fact]
    public void GetMoodBasedTools_Angry_ShouldIncludeCalmDown()
    {
        var tools = _service.GetMoodBasedTools(AgentMood.Angry);
        Assert.Contains(tools, t => t.Name == "calm_down");
    }

    [Fact]
    public void GetMoodBasedTools_Neutral_ShouldBeEmpty()
    {
        var tools = _service.GetMoodBasedTools(AgentMood.Neutral);
        Assert.Empty(tools);
    }

    [Fact]
    public void GetMoodBasedTools_Teasing_ShouldBeEmpty()
    {
        var tools = _service.GetMoodBasedTools(AgentMood.Teasing);
        Assert.Empty(tools);
    }

    [Fact]
    public void GetMoodBasedTools_Surprised_ShouldBeEmpty()
    {
        var tools = _service.GetMoodBasedTools(AgentMood.Surprised);
        Assert.Empty(tools);
    }

    // ========== 计时器 ==========

    [Fact]
    public void StartTimer_ShouldSetStatusRunning()
    {
        _service.StartTimerAsync(60, () => { });
        Assert.Equal(TimerStatus.Running, _service.GetTimerStatus());
        _service.StopTimer();
    }

    [Fact]
    public void StopTimer_ShouldSetStatusIdle()
    {
        _service.StartTimerAsync(60, () => { });
        _service.StopTimer();
        Assert.Equal(TimerStatus.Idle, _service.GetTimerStatus());
    }

    [Fact]
    public void TimerRemaining_ShouldReturnCorrectValue()
    {
        _service.StartTimerAsync(120, () => { });
        Assert.Equal(120, _service.GetTimerRemaining());
        _service.StopTimer();
    }

    [Fact]
    public void TogglePause_ShouldPauseAndResume()
    {
        _service.StartTimerAsync(60, () => { });
        Assert.Equal(TimerStatus.Running, _service.GetTimerStatus());

        _service.TogglePauseTimer();
        Assert.Equal(TimerStatus.Paused, _service.GetTimerStatus());

        _service.TogglePauseTimer();
        Assert.Equal(TimerStatus.Running, _service.GetTimerStatus());

        _service.StopTimer();
    }

    [Fact]
    public void TimerStatus_Initial_ShouldBeIdle()
    {
        Assert.Equal(TimerStatus.Idle, _service.GetTimerStatus());
    }

    // ========== 执行工具 ==========

    [Fact]
    public async Task ExecuteTool_Timer_ShouldReturnSuccess()
    {
        var result = await _service.ExecuteToolAsync("timer", "{\"seconds\": 60}");
        Assert.True(result.Success);
        Assert.Contains("60", result.Data);
        _service.StopTimer();
    }

    [Fact]
    public async Task ExecuteTool_Compliment_ShouldReturnSuccess()
    {
        var result = await _service.ExecuteToolAsync("compliment", "{}");
        Assert.True(result.Success);
        Assert.Contains("compliment", result.Data);
    }

    [Fact]
    public async Task ExecuteTool_Pet_ShouldReturnSuccess()
    {
        var result = await _service.ExecuteToolAsync("pet", "{}");
        Assert.True(result.Success);
        Assert.Contains("Touched", result.Data);
    }

    [Fact]
    public async Task ExecuteTool_Weather_ShouldReturnSuccess()
    {
        var result = await _service.ExecuteToolAsync("weather", "{\"city\": \"北京\"}");
        Assert.True(result.Success);
        Assert.Contains("City", result.Data);
        Assert.Contains("CurrentTemp", result.Data);
    }

    [Fact]
    public async Task ExecuteTool_NonExisting_ShouldReturnFailure()
    {
        var result = await _service.ExecuteToolAsync("nonexistent", "");
        Assert.False(result.Success);
        Assert.Contains("未知工具", result.Error);
    }

    [Fact]
    public async Task ExecuteTool_ListPlugins_ShouldReturnSuccess()
    {
        var result = await _service.ExecuteToolAsync("list_plugins", "{}");
        Assert.True(result.Success);
    }

    // ========== 情绪工具执行 ==========

    [Fact]
    public async Task ExecuteTool_Hug_ShouldSetMoodTouched()
    {
        var result = await _service.ExecuteToolAsync("hug", "{}");
        Assert.True(result.Success);
        Assert.Equal(AgentMood.Touched, _moodTracker.LastSetMood);
    }

    [Fact]
    public async Task ExecuteTool_Dance_ShouldSetMoodHappy()
    {
        var result = await _service.ExecuteToolAsync("dance", "{}");
        Assert.True(result.Success);
        Assert.Equal(AgentMood.Happy, _moodTracker.LastSetMood);
    }

    [Fact]
    public async Task ExecuteTool_CalmDown_ShouldSetMoodNeutral()
    {
        var result = await _service.ExecuteToolAsync("calm_down", "{}");
        Assert.True(result.Success);
        Assert.Equal(AgentMood.Neutral, _moodTracker.LastSetMood);
    }

    // ========== 获取夸奖 ==========

    [Fact]
    public async Task GetRandomCompliment_ShouldReturnNonEmpty()
    {
        var compliment = await _service.GetRandomComplimentAsync();
        Assert.False(string.IsNullOrEmpty(compliment));
    }

    // ========== 摸摸她 ==========

    [Fact]
    public async Task Pet_ShouldSetMoodTouched()
    {
        await _service.PetAsync();
        Assert.Equal(AgentMood.Touched, _moodTracker.LastSetMood);
    }

    [Fact]
    public async Task Pet_ShouldReturnNonEmpty()
    {
        var response = await _service.PetAsync();
        Assert.False(string.IsNullOrEmpty(response));
    }

    // ========== 天气预报 ==========

    [Fact]
    public async Task GetWeather_ShouldReturnValidData()
    {
        var weather = await _service.GetWeatherAsync("北京");
        Assert.Equal("北京", weather.City);
        Assert.False(string.IsNullOrEmpty(weather.CurrentTemp));
        Assert.False(string.IsNullOrEmpty(weather.Condition));
    }

    [Fact]
    public async Task GetWeather_EmptyCity_ShouldReturnUnknown()
    {
        var weather = await _service.GetWeatherAsync();
        Assert.Equal("未知城市", weather.City);
    }

    // ========== ListPlugins ==========

    [Fact]
    public async Task ListPlugins_ShouldReturnEmpty()
    {
        var plugins = await _service.ListPluginsAsync();
        Assert.Empty(plugins);
    }
}

/// <summary>
/// 模拟 LlmClient，返回固定夸奖语
/// </summary>
public class FakeLlmClient : LlmClient
{
    public FakeLlmClient() : base() { }

    // 重写 SendChatAsync 以跳过实际 API 调用
    public override async Task<string> SendChatAsync(string providerName, string model, string prompt)
    {
        return await Task.FromResult("你今天的笑容特别好看，像阳光一样温暖～");
    }
}

/// <summary>
/// 模拟情绪追踪器
/// </summary>
public class FakeMoodTracker : IAgentMoodTracker
{
    public AgentMood CurrentMood { get; private set; } = AgentMood.Neutral;
    public AgentMood? LastSetMood { get; private set; }

    public event EventHandler<AgentMood>? MoodChanged;

    public void SetMood(AgentMood mood)
    {
        LastSetMood = mood;
        CurrentMood = mood;
        MoodChanged?.Invoke(this, mood);
    }

    public void UpdateMoodByEvent(string eventType) { }
    public string GetMoodImagePath() => "";
}

/// <summary>
/// 模拟 PromptFormatter
/// </summary>
public class FakePromptFormatter : IPromptFormatter
{
    public string Format(Dictionary<string, string> variables)
    {
        return "请说一句夸奖用户的话，要温暖真诚";
    }
}
