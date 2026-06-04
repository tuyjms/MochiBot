using System.Text.Json;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using static MochiBot.Src.Core.Constants;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// Action 执行器
    /// 负责解析和执行 LLM 返回的 actions 数组
    /// 统一调度 tool_call / plugin_call / mcp_call / mood_change / midterm_memory / animation
    /// 执行结果自动通过回调记录到短期记忆
    /// </summary>
    public class ActionExecutor
    {
        // 短期记忆中的系统消息标签
        public const string TagMidTermMemory = "[中期记忆]";
        public const string TagToolExecution = "[工具执行]";
        public const string TagPluginExecution = "[插件执行]";
        public const string TagMcpExecution = "[MCP执行]";

        private readonly IToolService _toolService;
        private readonly Action<AgentMood> _onMoodChange;
        private readonly Action<string, string> _onMemoryLog;
        private readonly Action<string> _onAnimation;

        /// <summary>工具结果截断上限（避免短期记忆被撑爆）</summary>
        private const int ToolResultMaxLength = 500;

        public ActionExecutor(
            IToolService toolService,
            Action<AgentMood> onMoodChange,
            Action<string, string>? onMemoryLog = null,
            Action<string>? onAnimation = null)
        {
            _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
            _onMoodChange = onMoodChange ?? throw new ArgumentNullException(nameof(onMoodChange));
            _onMemoryLog = onMemoryLog ?? ((_, _) => { });
            _onAnimation = onAnimation ?? (_ => { });
        }

        /// <summary>
        /// 执行 actions 数组，返回从 reply 工具中提取的回复文本
        /// </summary>
        public async Task<string> ExecuteActionsAsync(List<AgentAction>? actions, int maxActions = 10)
        {
            var replyText = string.Empty;
            if (actions == null || actions.Count == 0) return replyText;

            var count = 0;

            foreach (var action in actions)
            {
                if (count >= maxActions) break;
                count++;

                try
                {
                    switch (action.Type)
                    {
                        case ActionTypes.ToolCall:
                            replyText = await HandleToolCallAsync(action, replyText);
                            break;

                        case ActionTypes.PluginCall:
                            await HandlePluginCallAsync(action);
                            break;

                        case ActionTypes.McpCall:
                            await HandleMcpCallAsync(action);
                            break;

                        case ActionTypes.MoodChange:
                            HandleMoodChange(action);
                            break;

                        case ActionTypes.MidtermMemory:
                            HandleMidtermMemory(action);
                            break;

                        case ActionTypes.Animation:
                            HandleAnimation(action);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ActionExecutor] 执行错误: {action.Type}/{action.Name}: {ex.Message}");
                }
            }

            return replyText;
        }

        /// <summary>
        /// 解析 LLM 响应，提取 actions 数组
        /// </summary>
        public static List<AgentAction>? ParseActions(string response)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("actions", out var actionsElement) &&
                    actionsElement.ValueKind == JsonValueKind.Array)
                {
                    var actions = new List<AgentAction>();
                    foreach (var item in actionsElement.EnumerateArray())
                    {
                        var action = new AgentAction();

                        if (item.TryGetProperty("type", out var typeProp))
                            action.Type = typeProp.GetString() ?? string.Empty;

                        if (item.TryGetProperty("name", out var nameProp))
                            action.Name = nameProp.GetString();

                        if (item.TryGetProperty("server_name", out var serverProp))
                            action.ServerName = serverProp.GetString();

                        if (item.TryGetProperty("mood", out var moodProp))
                            action.Mood = moodProp.GetString();

                        if (item.TryGetProperty("description", out var descProp))
                            action.Description = descProp.GetString();

                        if (item.TryGetProperty("animation", out var animProp))
                            action.Animation = animProp.GetString();

                        if (item.TryGetProperty("importance", out var impProp))
                            action.Importance = impProp.GetInt32();

                        // parameters 可能是对象或字符串，统一转为 JSON 字符串
                        if (item.TryGetProperty("parameters", out var paramsProp))
                        {
                            action.Parameters = paramsProp.ValueKind switch
                            {
                                JsonValueKind.String => paramsProp.GetString(),
                                JsonValueKind.Object or JsonValueKind.Array => paramsProp.GetRawText(),
                                _ => paramsProp.GetRawText()
                            };
                        }

                        actions.Add(action);
                    }
                    return actions;
                }
            }
            catch (JsonException)
            {
                // JSON 解析失败，返回 null
            }

            return null;
        }

        /// <summary>
        /// 从 LLM 响应中提取 reply 字段（非结构化模式使用）
        /// </summary>
        public static string? ExtractReply(string response)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("reply", out var replyElement))
                    return replyElement.GetString();
            }
            catch (JsonException) { }
            return null;
        }

        // ========== 私有处理方法 ==========

        private async Task<string> HandleToolCallAsync(AgentAction action, string currentReply)
        {
            // 如果是 reply 工具，提取回复文本
            if (action.Name == Tools.Reply && !string.IsNullOrEmpty(action.Parameters))
            {
                try
                {
                    using var doc = JsonDocument.Parse(action.Parameters);
                    if (doc.RootElement.TryGetProperty("reply_text", out var replyElement))
                    {
                        return replyElement.GetString() ?? string.Empty;
                    }
                }
                catch
                {
                    // 解析失败则忽略
                }
                return currentReply;
            }

            // 其他工具通过 ToolService 统一调度
            var result = await _toolService.ExecuteToolAsync(
                action.Name ?? "",
                action.Parameters ?? "{}");

            System.Diagnostics.Debug.WriteLine(
                $"[ActionExecutor] 工具执行: {action.Name}: {(result.Success ? "成功" : $"失败: {result.Error}")}");

            // 记录工具执行结果到短期记忆（LLM 工具调用循环依赖此数据）
            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                var truncated = result.Data.Length > ToolResultMaxLength
                    ? result.Data[..ToolResultMaxLength] + "...(截断)"
                    : result.Data;
                _onMemoryLog(TagToolExecution, $"{action.Name} → {truncated}");
            }
            else
            {
                _onMemoryLog(TagToolExecution, action.Name ?? "");
            }

            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                try
                {
                    using var doc = JsonDocument.Parse(result.Data);
                    if (doc.RootElement.TryGetProperty("animation", out var animProp))
                    {
                        var animationName = animProp.GetString();
                        if (!string.IsNullOrEmpty(animationName))
                        {
                            _onAnimation(animationName);
                        }
                    }
                }
                catch { }
            }

            return currentReply;
        }

        private async Task HandlePluginCallAsync(AgentAction action)
        {
            var result = await _toolService.ExecuteToolAsync(
                action.Name ?? "",
                action.Parameters ?? "{}");

            System.Diagnostics.Debug.WriteLine(
                $"[ActionExecutor] 插件执行: {action.Name}: {(result.Success ? "成功" : $"失败: {result.Error}")}");

            // 记录插件执行结果到短期记忆（LLM 工具调用循环依赖此数据）
            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                var truncated = result.Data.Length > ToolResultMaxLength
                    ? result.Data[..ToolResultMaxLength] + "...(截断)"
                    : result.Data;
                _onMemoryLog(TagPluginExecution, $"{action.Name} → {truncated}");
            }
            else
            {
                _onMemoryLog(TagPluginExecution, action.Name ?? "");
            }
        }

        private Task HandleMcpCallAsync(AgentAction action)
        {
            // MCP 工具调用（留空，作为 feature 后续实现）
            System.Diagnostics.Debug.WriteLine(
                $"[ActionExecutor] MCP执行: {action.ServerName}/{action.Name}（暂未实现）");

            // 记录 MCP 执行到短期记忆
            _onMemoryLog(TagMcpExecution, $"{action.ServerName}/{action.Name}");
            return Task.CompletedTask;
        }

        private void HandleMoodChange(AgentAction action)
        {
            // LLM 可能将情绪值放在 Mood 或 Name 字段
            var moodStr = action.Mood ?? action.Name;
            if (Enum.TryParse<AgentMood>(moodStr, true, out var mood))
            {
                _onMoodChange(mood);
            }
        }

        private void HandleMidtermMemory(AgentAction action)
        {
            _onMemoryLog(TagMidTermMemory, action.Description ?? "");
        }

        private void HandleAnimation(AgentAction action)
        {
            if (!string.IsNullOrEmpty(action.Animation))
            {
                _onAnimation(action.Animation);
            }
        }
    }
}
