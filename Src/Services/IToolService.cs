using MochiBot.Src.EventModels;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 工具定义，按 MCP 标准描述工具信息
    /// </summary>
    public class ToolDefinition
    {
        /// <summary>工具名称（唯一标识，供LLM调用时使用）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>工具描述（供LLM理解工具用途）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>输入参数 schema（JSON Schema 格式，描述参数结构）</summary>
        public Dictionary<string, object> InputSchema { get; set; } = new();
    }

    /// <summary>
    /// 工具执行结果
    /// </summary>
    public class ToolResult
    {
        /// <summary>是否成功</summary>
        public bool Success { get; set; }

        /// <summary>结果数据（JSON格式）</summary>
        public string Data { get; set; } = string.Empty;

        /// <summary>错误信息（失败时）</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// 计时器状态
    /// </summary>
    public enum TimerStatus
    {
        Idle,       // 空闲
        Running,    // 运行中
        Paused,     // 暂停
        Completed   // 已完成
    }

    /// <summary>
    /// 工具调度器接口
    /// 统一管理基础工具、心情特色工具、DLLMOD插件、MCP工具
    /// </summary>
    public interface IToolService
    {
        /// <summary>获取所有已注册的基础工具定义（供LLM理解可用工具）</summary>
        List<ToolDefinition> GetToolDefinitions();

        /// <summary>获取工具调用格式说明（供 LLM 理解 actions 返回格式）</summary>
        string GetFormatInstruction();

        /// <summary>根据当前情绪获取附加工具定义</summary>
        /// <param name="currentMood">当前情绪</param>
        List<ToolDefinition> GetMoodBasedTools(AgentMood currentMood);

        /// <summary>列出所有已加载的DLLMOD插件及其描述（供LLM通过 list_plugins 调用）</summary>
        Task<List<ToolDefinition>> ListPluginsAsync();

        /// <summary>列出所有已注册的MCP服务器工具（供LLM通过 list_plugins 调用）</summary>
        Task<List<ToolDefinition>> ListMcpToolsAsync();

        /// <summary>统一执行工具调度（自动识别工具类型）</summary>
        /// <param name="toolName">工具名称</param>
        /// <param name="parameters">参数（JSON字符串）</param>
        Task<ToolResult> ExecuteToolAsync(string toolName, string parameters);

        /// <summary>加载DLLMOD插件</summary>
        /// <param name="modDirectory">插件目录</param>
        Task LoadModsAsync(string modDirectory);

        // ========== 计时器 ==========

        /// <summary>启动倒计时</summary>
        /// <param name="seconds">倒计时秒数</param>
        /// <param name="onComplete">倒计时结束回调</param>
        Task StartTimerAsync(int seconds, Action onComplete);

        /// <summary>停止当前计时器</summary>
        void StopTimer();

        /// <summary>暂停/恢复计时器</summary>
        void TogglePauseTimer();

        /// <summary>获取计时器剩余时间（秒）</summary>
        int GetTimerRemaining();

        /// <summary>获取计时器状态</summary>
        TimerStatus GetTimerStatus();

    }
}
