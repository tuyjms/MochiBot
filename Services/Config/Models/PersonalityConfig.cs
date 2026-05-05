namespace catgirlwindow.Services.Config.Models;

/// <summary>
/// 人格配置根模型（对应 {人物名称}_person.json）
/// </summary>
public class PersonalityConfig
{
    /// <summary>人物名称（仅允许中文、英文、数字、下划线，且不能以数字开头）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>人物描述/背景故事</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>子人格列表</summary>
    public List<SubPersonality> Personalities { get; set; } = new();
}

/// <summary>
/// 子人格定义
/// </summary>
public class SubPersonality
{
    /// <summary>子人格名称（如：温柔、毒舌、活泼）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>子人格描述/行为规则</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 对话主力LLM模型列表（按优先级排序，支持故障转移）
    /// 格式："{提供商名称}/{模型名称}"
    /// 示例：["LocalLMStudio/qwen2.5-7b", "OpenAI/gpt-4o-mini"]
    /// 当第一个模型请求失败时，自动 fallback 到下一个模型
    /// </summary>
    public List<string> ChatModels { get; set; } = new();

    /// <summary>
    /// 图转文字LLM模型列表（可选，按优先级排序，支持故障转移）
    /// 格式同上，为空时使用 ChatModels 的第一个
    /// </summary>
    public List<string>? VisionModels { get; set; }
}
