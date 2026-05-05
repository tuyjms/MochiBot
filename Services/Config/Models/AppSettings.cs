namespace catgirlwindow.Services.Config.Models;

/// <summary>
/// 应用级配置
/// </summary>
public class AppSettings
{
    /// <summary>默认使用的提供商名称</summary>
    public string DefaultProvider { get; set; } = "LocalLMStudio";

    /// <summary>对话模式使用的模型名称（默认值，人格配置可覆盖）</summary>
    public string ChatModel { get; set; } = "default";

    /// <summary>函数模式使用的模型名称（可指定更便宜的模型）</summary>
    public string FunctionModel { get; set; } = "default";

    /// <summary>当前激活的人格名称（对应 Resources/Personalities/ 下的 {名称}_person.json）</summary>
    public string ActivePersonality { get; set; } = "default";

    /// <summary>是否启用LLM结构化响应解析</summary>
    public bool EnableStructuredResponse { get; set; } = true;

    /// <summary>单次LLM响应最大执行动作数</summary>
    public int MaxActionsPerResponse { get; set; } = 5;

    /// <summary>对话时是否允许LLM主动录入中期记忆</summary>
    public bool EnableMidTermMemoryOnChat { get; set; } = true;

    /// <summary>对话时是否检索长期记忆注入上下文</summary>
    public bool EnableLongTermRecall { get; set; } = true;

    /// <summary>日志级别</summary>
    public string LogLevel { get; set; } = "Info";

    /// <summary>是否启用日志文件输出</summary>
    public bool LogToFile { get; set; } = true;

    /// <summary>是否启用日志控制台输出</summary>
    public bool LogToConsole { get; set; } = true;
}
