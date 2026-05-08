using System.Text.Json;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Services;
using MochiBot.Src.EventModels;

namespace MochiBot.Src.Services.Tool
{
    /// <summary>
    /// 工具调度器实现
    /// 统一管理基础工具、心情特色工具、DLLMOD插件、MCP工具
    /// </summary>
    public class ToolService : IToolService, IDisposable
    {
        private readonly IDllModLoader _modLoader;
        private readonly IConfigReader _configReader;
        private readonly Random _random = new();

        // 心情附加工具
        private static readonly Dictionary<AgentMood, ToolDefinition> MoodTools = new()
        {
            [AgentMood.Sad] = new ToolDefinition
            {
                Name = "cry",
                Description = "播放哭泣动画，表达委屈难过的情绪（当前心情委屈时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            },
            [AgentMood.Happy] = new ToolDefinition
            {
                Name = "dance",
                Description = "播放跳舞动画，表达开心愉悦的情绪（当前心情开心时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            },
            [AgentMood.Sleepy] = new ToolDefinition
            {
                Name = "yawn",
                Description = "播放打哈欠动画，表达困倦想睡的情绪（当前心情困倦时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            },
            [AgentMood.Touched] = new ToolDefinition
            {
                Name = "blush",
                Description = "播放脸红动画，表达害羞感动的情绪（当前心情感动时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            },
            [AgentMood.Angry] = new ToolDefinition
            {
                Name = "stomp",
                Description = "播放跺脚动画，表达生气不满的情绪（当前心情生气时可用）",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } }
            }
        };

        public ToolService(LlmClient llmClient, IConfigReader configReader, IDllModLoader? modLoader = null)
        {
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _modLoader = modLoader ?? new DllModLoader();
        }

        /// <summary>获取工具调用格式的 Prompt 说明（与基础工具描述一起使用）</summary>
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
                new() { Name = "list_plugins", Description = "列出所有已加载的DLLMOD插件和MCP服务器工具及其描述", 
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() }, { "required", Array.Empty<string>() } } },
                new() { Name = "reply", Description = "回复用户说的话。如果不调用此工具，则表示不回复（保持沉默）。调用此工具时，reply_text 参数为你要说的话",
                InputSchema = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object> { { "reply_text", new Dictionary<string, object> { { "type", "string" }, { "description", "你要对用户说的话" } } } } }, { "required", new[] { "reply_text" } } } }
            };

            return tools;
        }

        /// <summary>获取工具调用格式说明（供 LLM 理解 actions 返回格式）</summary>
        public string GetFormatInstruction()
        {
            return @"【工具调用格式说明】
你必须返回一个 JSON 对象，包含 actions 数组。格式如下：
{
  ""actions"": [
    {""type"": ""tool_call"", ""name"": ""reply"", ""parameters"": {""reply_text"": ""你要说的话""}},
    {""type"": ""tool_call"", ""name"": ""timer"", ""parameters"": {""seconds"": 300}}
  ]
}

actions 数组中每个元素的 type 可以是：
1. tool_call - 调用基础工具或心情附加工具（包括 reply）
2. plugin_call - 调用已加载的DLLMOD插件（需先调用 list_plugins 获取列表）
3. mcp_call - 调用MCP服务器工具（需先调用 list_plugins 获取列表）
4. mood_change - 切换你的情绪（happy/sad/sleepy/touched/angry）
5. midterm_memory - 记录一条重要信息到中期记忆

【重要规则】
- 回复用户必须通过调用 reply 工具，在 reply_text 参数中填写你要说的话。
- 如果你不想回复（比如没什么好说的、或者不想打扰用户），就不要调用 reply 工具，保持沉默。
- 其他工具（timer 等）可以配合 reply 一起使用，也可以单独使用。";
        }

        public List<ToolDefinition> GetMoodBasedTools(AgentMood currentMood)
        {
            return MoodTools.TryGetValue(currentMood, out var tool) ? new List<ToolDefinition> { tool } : new List<ToolDefinition>();
        }

        public async Task<List<ToolDefinition>> ListPluginsAsync()
        {
            var mods = _modLoader.GetLoadedMods();
            return mods.Select(m => new ToolDefinition
            {
                Name = m.Name,
                Description = m.Description,
                InputSchema = new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>() },
                    { "required", Array.Empty<string>() }
                }
            }).ToList();
        }

        public Task<List<ToolDefinition>> ListMcpToolsAsync()
        {
            // MCP 工具列表（留空，作为 feature 后续实现）
            return Task.FromResult(new List<ToolDefinition>());
        }

        public async Task LoadModsAsync(string modDirectory)
        {
            await _modLoader.LoadModsAsync(modDirectory);
        }

        /// <summary>
        /// 统一执行工具调度
        /// 自动识别工具类型：基础工具 -> 心情工具 -> DLLMOD插件
        /// </summary>
        public async Task<ToolResult> ExecuteToolAsync(string toolName, string parameters)
        {
            try
            {
                // 1. 尝试基础工具
                var result = await ExecuteBaseToolAsync(toolName, parameters);
                if (result != null) return result;

                // 2. 尝试心情动画工具
                result = ExecuteMoodTool(toolName);
                if (result != null) return result;

                // 3. 尝试 DLLMOD 插件
                try
                {
                    var modResult = await _modLoader.ExecuteModAsync(toolName, parameters);
                    return new ToolResult { Success = true, Data = modResult };
                }
                catch (KeyNotFoundException)
                {
                    // 插件未找到，继续
                }

                return new ToolResult { Success = false, Error = $"未知工具或插件: {toolName}" };
            }
            catch (Exception ex)
            {
                return new ToolResult { Success = false, Error = $"执行工具 '{toolName}' 时出错: {ex.Message}" };
            }
        }

        /// <summary>执行基础工具</summary>
        private async Task<ToolResult?> ExecuteBaseToolAsync(string toolName, string parameters)
        {
            return toolName switch
            {
                "timer" => await ExecuteTimerAsync(parameters),
                "murmur" => await ExecuteMurmurAsync(),
                "list_plugins" => await ExecuteListPluginsAsync(),
                _ => null
            };
        }

        /// <summary>执行心情动画工具</summary>
        private static ToolResult? ExecuteMoodTool(string toolName)
        {
            return toolName switch
            {
                "cry" => new ToolResult { Success = true, Data = "{\"animation\":\"cry\"}" },
                "dance" => new ToolResult { Success = true, Data = "{\"animation\":\"dance\"}" },
                "yawn" => new ToolResult { Success = true, Data = "{\"animation\":\"yawn\"}" },
                "blush" => new ToolResult { Success = true, Data = "{\"animation\":\"blush\"}" },
                "stomp" => new ToolResult { Success = true, Data = "{\"animation\":\"stomp\"}" },
                _ => null
            };
        }

        private async Task<ToolResult> ExecuteTimerAsync(string parameters)
        {
            try
            {
                using var doc = JsonDocument.Parse(parameters);
                if (!doc.RootElement.TryGetProperty("seconds", out var secondsProp))
                {
                    return new ToolResult { Success = false, Error = "缺少 seconds 参数" };
                }
                var seconds = secondsProp.GetInt32();
                
                // 启动实际的倒计时（fire-and-forget）
                _ = StartRealTimerAsync(seconds);
                
                return new ToolResult { Success = true, Data = JsonSerializer.Serialize(new { message = $"已启动 {seconds} 秒倒计时", seconds, status = "running" }) };
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolService] timer 参数解析失败: {ex.Message}, parameters={parameters}");
                return new ToolResult { Success = false, Error = $"timer 参数解析失败: {ex.Message}" };
            }
        }

        private static async Task StartRealTimerAsync(int seconds)
        {
            try
            {
                await Task.Delay(seconds * 1000);
                System.Diagnostics.Debug.WriteLine($"[ToolService] 倒计时结束！已过去 {seconds} 秒");
                // 倒计时结束后的提醒逻辑由外部事件处理
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolService] 倒计时异常: {ex.Message}");
            }
        }

        private Task<ToolResult> ExecuteMurmurAsync()
        {
            var settings = _configReader.GetModuleSettings();
            var texts = settings.MurmurTexts;
            if (texts == null || texts.Count == 0)
            {
                texts = new List<string> { "那个…你在忙吗？我、我只是想你了…" };
            }
            var text = texts[_random.Next(texts.Count)];
            return Task.FromResult(new ToolResult { Success = true, Data = JsonSerializer.Serialize(new { murmur = text }) });
        }

        private async Task<ToolResult> ExecuteListPluginsAsync()
        {
            var plugins = await ListPluginsAsync();
            var mcpTools = await ListMcpToolsAsync();
            return new ToolResult { Success = true, Data = JsonSerializer.Serialize(new { plugins, mcp_tools = mcpTools }) };
        }

        // ========== 计时器（已移除，由外部管理） ==========

        public Task StartTimerAsync(int seconds, Action onComplete)
        {
            // 计时器功能已从 ToolService 中移除
            // 由外部 TimerService 或调用方管理
            return Task.CompletedTask;
        }

        public void StopTimer() { }
        public void TogglePauseTimer() { }
        public int GetTimerRemaining() => 0;
        public TimerStatus GetTimerStatus() => TimerStatus.Idle;

        // ========== 工具功能实现 ==========

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
