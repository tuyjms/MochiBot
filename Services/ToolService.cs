using System.Text.Json;
using System.Threading;
using catgirlwindow.Models;
using Timer = System.Threading.Timer;

namespace catgirlwindow.Services;

/// <summary>
/// 工具功能服务实现
/// </summary>
public class ToolService : IToolService, IDisposable
{
    private readonly LlmClient _llmClient;
    private readonly IAgentMoodTracker _moodTracker;
    private readonly IPromptFormatter _formatter;
    private readonly Random _random = new();

    // 计时器
    private Timer? _timer;
    private int _remainingSeconds;
    private TimerStatus _timerStatus = TimerStatus.Idle;
    private Action? _onComplete;
    private readonly object _timerLock = new();

    // 本地夸奖语
    private static readonly string[] ComplimentTemplates =
    {
        "你今天的笑容特别好看，像阳光一样温暖～",
        "你真的太棒了，每次和你聊天都很开心！",
        "你知道吗？你认真做事的样子特别迷人～",
        "有你在真好，你是我最重要的人！",
        "你今天看起来特别精神，是不是有什么好事呀？",
        "你总是能让我感到安心，谢谢你～",
        "你的眼睛里有星星，特别好看！",
        "和你在一起的每一刻都很幸福～",
        "你真的很聪明，什么问题都难不倒你！",
        "你是我见过最温柔的人～"
    };

    // 摸摸她回应
    private static readonly string[] PetResponses =
    {
        "呜…被摸头了好开心～",
        "嘿嘿，再摸摸嘛～",
        "好温暖的感觉…抱抱你～",
        "被摸头的时候最幸福了！",
        "唔…好舒服，不要停～"
    };

    // 心情附加工具
    private static readonly Dictionary<AgentMood, ToolDefinition> MoodTools = new()
    {
        [AgentMood.Sad] = new ToolDefinition
        {
            Name = "hug",
            Description = "拥抱AI女友，她会感到温暖和安慰（当前心情Sad时可用）",
            InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", new string[] { } } }
        },
        [AgentMood.Happy] = new ToolDefinition
        {
            Name = "dance",
            Description = "和AI女友一起跳舞，她会更开心（当前心情Happy时可用）",
            InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", new string[] { } } }
        },
        [AgentMood.Sleepy] = new ToolDefinition
        {
            Name = "tuck_in",
            Description = "给AI女友盖好被子，哄她睡觉（当前心情Sleepy时可用）",
            InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", new string[] { } } }
        },
        [AgentMood.Touched] = new ToolDefinition
        {
            Name = "cuddle",
            Description = "和AI女友依偎在一起（当前心情Touched时可用）",
            InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", new string[] { } } }
        },
        [AgentMood.Angry] = new ToolDefinition
        {
            Name = "calm_down",
            Description = "安抚生气的AI女友（当前心情Angry时可用）",
            InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", new string[] { } } }
        }
    };

    public ToolService(LlmClient llmClient, IAgentMoodTracker moodTracker, IPromptFormatter formatter)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _moodTracker = moodTracker ?? throw new ArgumentNullException(nameof(moodTracker));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    public List<ToolDefinition> GetToolDefinitions()
    {
        return new List<ToolDefinition>
        {
            new() { Name = "timer", Description = "启动一个倒计时，倒计时结束后会提醒用户", InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object> { { "seconds", new Dictionary<string, object> { { "type", "integer" }, { "description", "倒计时秒数" }, { "minimum", 10 }, { "maximum", 3600 } } } } }, { "required", new[] { "seconds" } } } },
            new() { Name = "compliment", Description = "随机说一句夸奖用户的话，让用户感到被鼓励和温暖", InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", new string[] { } } } },
            new() { Name = "pet", Description = "摸摸AI女友的头，她会感到开心和感动", InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", new string[] { } } } },
            new() { Name = "weather", Description = "查询指定城市的当前天气和今日天气预报", InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object> { { "city", new Dictionary<string, object> { { "type", "string" }, { "description", "城市名称，为空则自动获取IP所在城市" } } } } }, { "required", new string[] { } } } },
            new() { Name = "list_plugins", Description = "列出所有已加载的JS插件和MCP服务器工具及其描述", InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", new string[] { } } } }
        };
    }

    public List<ToolDefinition> GetMoodBasedTools(AgentMood currentMood)
    {
        return MoodTools.TryGetValue(currentMood, out var tool) ? new List<ToolDefinition> { tool } : new List<ToolDefinition>();
    }

    public Task<List<ToolDefinition>> ListPluginsAsync()
    {
        return Task.FromResult(new List<ToolDefinition>());
    }

    public async Task<ToolResult> ExecuteToolAsync(string toolName, string parameters)
    {
        try
        {
            return toolName switch
            {
                "timer" => await ExecuteTimerAsync(parameters),
                "compliment" => await ExecuteComplimentAsync(),
                "pet" => await ExecutePetAsync(),
                "weather" => await ExecuteWeatherAsync(parameters),
                "list_plugins" => await ExecuteListPluginsAsync(),
                "hug" => await ExecuteHugAsync(),
                "dance" => await ExecuteDanceAsync(),
                "tuck_in" => await ExecuteTuckInAsync(),
                "cuddle" => await ExecuteCuddleAsync(),
                "calm_down" => await ExecuteCalmDownAsync(),
                _ => new ToolResult { Success = false, Error = $"未知工具: {toolName}" }
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = $"执行工具 '{toolName}' 时出错: {ex.Message}" };
        }
    }

    private async Task<ToolResult> ExecuteTimerAsync(string parameters)
    {
        using var doc = JsonDocument.Parse(parameters);
        var seconds = doc.RootElement.GetProperty("seconds").GetInt32();
        await StartTimerAsync(seconds, () => { });
        return new ToolResult { Success = true, Data = JsonSerializer.Serialize(new { message = $"已启动 {seconds} 秒倒计时", seconds, status = "running" }) };
    }

    private async Task<ToolResult> ExecuteComplimentAsync()
    {
        var compliment = await GetRandomComplimentAsync();
        return new ToolResult { Success = true, Data = JsonSerializer.Serialize(new { compliment }) };
    }

    private async Task<ToolResult> ExecutePetAsync()
    {
        var response = await PetAsync();
        return new ToolResult { Success = true, Data = JsonSerializer.Serialize(new { response, mood = "Touched" }) };
    }

    private async Task<ToolResult> ExecuteWeatherAsync(string parameters)
    {
        string city = "";
        try { using var doc = JsonDocument.Parse(parameters); if (doc.RootElement.TryGetProperty("city", out var c)) city = c.GetString() ?? ""; } catch { }
        var weather = await GetWeatherAsync(city);
        return new ToolResult { Success = true, Data = JsonSerializer.Serialize(weather) };
    }

    private async Task<ToolResult> ExecuteListPluginsAsync()
    {
        var plugins = await ListPluginsAsync();
        return new ToolResult { Success = true, Data = JsonSerializer.Serialize(new { plugins = new List<object>(), mcp_tools = new List<object>() }) };
    }

    private Task<ToolResult> ExecuteHugAsync() { _moodTracker.SetMood(AgentMood.Touched); return Task.FromResult(new ToolResult { Success = true, Data = "{\"response\":\"被抱抱了好温暖…谢谢你～\",\"mood\":\"Touched\"}" }); }
    private Task<ToolResult> ExecuteDanceAsync() { _moodTracker.SetMood(AgentMood.Happy); return Task.FromResult(new ToolResult { Success = true, Data = "{\"response\":\"好呀好呀，一起跳舞吧～♪\",\"mood\":\"Happy\"}" }); }
    private Task<ToolResult> ExecuteTuckInAsync() { _moodTracker.SetMood(AgentMood.Sleepy); return Task.FromResult(new ToolResult { Success = true, Data = "{\"response\":\"唔…被盖好被子了，好暖和…晚安～\",\"mood\":\"Sleepy\"}" }); }
    private Task<ToolResult> ExecuteCuddleAsync() { _moodTracker.SetMood(AgentMood.Touched); return Task.FromResult(new ToolResult { Success = true, Data = "{\"response\":\"就这样依偎着…好幸福～\",\"mood\":\"Touched\"}" }); }
    private Task<ToolResult> ExecuteCalmDownAsync() { _moodTracker.SetMood(AgentMood.Neutral); return Task.FromResult(new ToolResult { Success = true, Data = "{\"response\":\"嗯…被你安抚了，我不生气了～\",\"mood\":\"Neutral\"}" }); }

    public Task StartTimerAsync(int seconds, Action onComplete)
    {
        lock (_timerLock)
        {
            StopTimer();
            _remainingSeconds = seconds;
            _timerStatus = TimerStatus.Running;
            _onComplete = onComplete;
            _timer = new Timer(TimerTick, null, 1000, 1000);
        }
        return Task.CompletedTask;
    }

    public void StopTimer()
    {
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = null;
            _remainingSeconds = 0;
            _timerStatus = TimerStatus.Idle;
            _onComplete = null;
        }
    }

    public void TogglePauseTimer()
    {
        lock (_timerLock)
        {
            if (_timerStatus == TimerStatus.Running) { _timer?.Dispose(); _timer = null; _timerStatus = TimerStatus.Paused; }
            else if (_timerStatus == TimerStatus.Paused) { _timerStatus = TimerStatus.Running; _timer = new Timer(TimerTick, null, 1000, 1000); }
        }
    }

    public int GetTimerRemaining()
    {
        lock (_timerLock) { return _remainingSeconds; }
    }

    public TimerStatus GetTimerStatus()
    {
        lock (_timerLock) { return _timerStatus; }
    }

    private void TimerTick(object? state)
    {
        lock (_timerLock)
        {
            if (_timerStatus != TimerStatus.Running) return;
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                _timer?.Dispose();
                _timer = null;
                _timerStatus = TimerStatus.Completed;
                var callback = _onComplete;
                _onComplete = null;
                callback?.Invoke();
            }
        }
    }

    public async Task<string> GetRandomComplimentAsync()
    {
        try
        {
            var template = _formatter.Format(new Dictionary<string, string> { { "type", "compliment" } });
            var result = await _llmClient.SendChatAsync("default", "gpt-4o-mini", template);
            if (!string.IsNullOrWhiteSpace(result)) return result.Trim();
        }
        catch { }
        return ComplimentTemplates[_random.Next(ComplimentTemplates.Length)];
    }

    public Task<string> PetAsync()
    {
        _moodTracker.SetMood(AgentMood.Touched);
        return Task.FromResult(PetResponses[_random.Next(PetResponses.Length)]);
    }

    public Task<WeatherInfo> GetWeatherAsync(string city = "")
    {
        return Task.FromResult(new WeatherInfo
        {
            City = string.IsNullOrEmpty(city) ? "未知城市" : city,
            CurrentTemp = "22°C",
            Condition = "晴",
            TodayHigh = "26°C",
            TodayLow = "18°C",
            Advice = "天气不错，适合外出活动～"
        });
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
