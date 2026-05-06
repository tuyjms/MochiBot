using System.Text.Json;
using catgirlwindow.Src.Agent;
using catgirlwindow.Src.Core.Models;
using Timer = System.Threading.Timer;

namespace catgirlwindow.Src.Services
{
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
        private readonly Lock _timerLock = new();

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
                Description = "拥抱AI女友，她会感到温暖和安慰（当前心情委屈时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            },
            [AgentMood.Happy] = new ToolDefinition
            {
                Name = "dance",
                Description = "和AI女友一起跳舞，她会更开心（当前心情开心时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            },
            [AgentMood.Sleepy] = new ToolDefinition
            {
                Name = "tuck_in",
                Description = "哄AI女友睡觉（当前心情困倦时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            },
            [AgentMood.Touched] = new ToolDefinition
            {
                Name = "cuddle",
                Description = "和感动的AI女友依偎在一起（当前心情感动时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            },
            [AgentMood.Angry] = new ToolDefinition
            {
                Name = "calm_down",
                Description = "安抚生气的AI女友（当前心情生气时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            }
        };

        public ToolService(LlmClient llmClient, IAgentMoodTracker moodTracker, IPromptFormatter formatter)
        {
            _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
            _moodTracker = moodTracker ?? throw new ArgumentNullException(nameof(moodTracker));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        }

        /// <summary>获取工具调用格式的 Prompt 说明（与基础工具描述一起使用）</summary
        public List<ToolDefinition> GetToolDefinitions()
        {
            var tools = new List<ToolDefinition>
            {
                new() { Name = "timer", Description = "启动一个倒计时，倒计时结束后会提醒用户", 
                        InputSchema = new Dictionary<string, object> { 
                            { "type", "object" },
                            { "properties", new Dictionary<string, object> { 
                            { "seconds", new Dictionary<string, object> {
                            { "type", "integer" }, 
                            { "description", "倒计时秒数" }, 
                            { "minimum", 10 }, { "maximum", 3600 } } } } },
                            { "required", new[] { "seconds" } } } },
                new() { Name = "compliment", Description = "随机说一句夸奖用户的话，让用户感到被鼓励和温暖", 
                    InputSchema = new Dictionary<string, object> {
                         { "type", "object" }, 
                         { "properties", new Dictionary<string, object>() }, 
                         { "required", Array.Empty<string>() } } },
                new() { Name = "pet", Description = "摸摸AI女友的头，她会感到开心和感动", 
                    InputSchema = new Dictionary<string, object> {
                         { "type", "object" }, 
                         { "properties", new Dictionary<string, object>() }, 
                         { "required", Array.Empty<string>() } } },
                new() { Name = "weather", Description = "查询指定城市的当前天气和今日天气预报", 
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object> { { "city", new Dictionary<string, object> { { "type", "string" }, { "description", "城市名称，为空则自动获取IP所在城市" } } } } }, { "required", Array.Empty<string>() } } },
                new() { Name = "list_plugins", Description = "列出所有已加载的JS插件和MCP服务器工具及其描述", 
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } } },
                new() { Name = "reply", Description = "回复用户说的话。如果不调用此工具，则表示不回复（保持沉默）。调用此工具时，reply_text 参数为你要说的话",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object> { { "reply_text", new Dictionary<string, object> { { "type", "string" }, { "description", "你要对用户说的话" } } } } }, { "required", new[] { "reply_text" } } } }
            };

            // 附加工具调用格式说明（作为虚拟工具注入，供 LLM 理解返回格式）
            tools.Add(new ToolDefinition
            {
                Name = "_format_instruction",
                Description = @"【工具调用格式说明】
你必须返回一个 JSON 对象，包含 actions 数组。格式如下：
{
  ""actions"": [
    {""type"": ""tool_call"", ""name"": ""reply"", ""parameters"": {""reply_text"": ""你要说的话""}},
    {""type"": ""tool_call"", ""name"": ""timer"", ""parameters"": {""seconds"": 300}}
  ]
}

actions 数组中每个元素的 type 可以是：
1. tool_call - 调用基础工具或心情附加工具（包括 reply）
2. plugin_call - 调用已加载的JS插件（需先调用 list_plugins 获取列表）
3. mcp_call - 调用MCP服务器工具（需先调用 list_plugins 获取列表）
4. mood_change - 切换你的情绪（happy/sad/sleepy/touched/angry）
5. midterm_memory - 记录一条重要信息到中期记忆

【重要规则】
- 回复用户必须通过调用 reply 工具，在 reply_text 参数中填写你要说的话。
- 如果你不想回复（比如没什么好说的、或者不想打扰用户），就不要调用 reply 工具，保持沉默。
- 其他工具（timer/compliment/pet/weather 等）可以配合 reply 一起使用，也可以单独使用。",
                InputSchema = new Dictionary<string, object>()
            });

            return tools;
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
                // 使用第一个可用的提供商
                var providers = _llmClient.GetAvailableProviders().ToList();
                var provider = providers.FirstOrDefault() ?? "default";
                var result = await _llmClient.SendChatAsync(provider, "gpt-4o-mini", template);
                if (!string.IsNullOrWhiteSpace(result)) return result.Trim();
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("[ToolService] LLM夸奖失败，使用本地模板");
            }
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
            GC.SuppressFinalize(this);
        }
    }
}
