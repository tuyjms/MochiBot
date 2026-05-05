namespace catgirlwindow.Services;

/// <summary>
/// Agent 状态摘要
/// </summary>
public class AgentStatus
{
    public string CurrentMood { get; set; } = string.Empty;
    public int ShortTermMemoryCount { get; set; }
    public int MidTermMemoryCount { get; set; }
    public int LongTermMemoryCount { get; set; }
    public bool IsProcessing { get; set; }
    public string LastEvent { get; set; } = string.Empty;
}

/// <summary>
/// Agent 核心协调层接口
/// 作为 LLM 与平台交互的唯一入口
/// </summary>
public interface IAgent
{
    // ========== 对话模式（LLM扮演女友） ==========

    /// <summary>处理用户输入消息</summary>
    /// <param name="userMessage">用户发送的消息</param>
    /// <returns>AI回复内容</returns>
    Task<string> ProcessUserInputAsync(string userMessage);

    /// <summary>处理自动事件（碎碎念、用眼提醒、深夜关怀）</summary>
    /// <param name="eventType">事件类型：murmur / eye_rest / late_night</param>
    /// <param name="eventData">事件附带数据（可选）</param>
    /// <returns>AI生成的回复内容</returns>
    Task<string> ProcessAutoEventAsync(string eventType, string? eventData = null);


    // ========== 函数模式（LLM作为纯函数） ==========

    /// <summary>总结短期记忆（溢出时调用）</summary>
    /// <param name="chatHistory">需要总结的对话历史</param>
    /// <returns>摘要文本</returns>
    Task<string> SummarizeMemoryAsync(string chatHistory);

    /// <summary>从事件描述中提取关键词（主谓宾）</summary>
    /// <param name="description">事件描述文本</param>
    /// <returns>三个关键词的元组</returns>
    Task<(string kw1, string kw2, string kw3)> ExtractKeywordsAsync(string description);

    /// <summary>评估一段对话的重要度（0-100）</summary>
    /// <param name="content">需要评估的内容</param>
    /// <returns>重要度分数</returns>
    Task<int> EvaluateImportanceAsync(string content);


    // ========== 工具/插件/MCP调用 ==========

    /// <summary>处理工具调用</summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="parameters">工具参数（JSON格式）</param>
    /// <returns>工具执行结果</returns>
    Task<string> ProcessToolCallAsync(string toolName, string parameters);

    /// <summary>处理JS插件调用</summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="parameters">插件参数（JSON格式）</param>
    /// <returns>插件执行结果</returns>
    Task<string> ProcessPluginCallAsync(string pluginName, string parameters);

    /// <summary>处理MCP服务器工具调用</summary>
    /// <param name="serverName">MCP服务器名称</param>
    /// <param name="toolName">工具名称</param>
    /// <param name="parameters">工具参数（JSON格式）</param>
    /// <returns>MCP工具执行结果</returns>
    Task<string> ProcessMcpCallAsync(string serverName, string toolName, string parameters);


    // ========== 状态查询 ==========

    /// <summary>获取当前Agent状态摘要</summary>
    AgentStatus GetStatus();
}
