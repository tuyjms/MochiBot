namespace MochiBot.Src.Core.Config.Models
{
    /// <summary>
    /// 人格配置根模型（对应 {人物名称}_person.json）
    /// 主人格包含模型配置和默认描述，子人格仅包含描述和权重
    /// </summary>
    public class PersonalityConfig
    {
        /// <summary>子人格权重总和必须为此值</summary>
        public const int SubPersonalityWeightSum = 100;

        /// <summary>有效的显示模式</summary>
        public static readonly string[] ValidDisplayModes = { "Gif", "Vrm" };

        /// <summary>人物名称（仅允许中文、英文、数字、下划线，且不能以数字开头）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>主人格描述/背景故事（默认使用，子人格按概率切换）</summary>
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

        /// <summary>
        /// 函数调用LLM模型列表（可选，按优先级排序，支持故障转移）
        /// 用于摘要、关键词提取、重要性评估等后台任务
        /// 格式同上，为空时使用 ChatModels 的第一个
        /// </summary>
        public List<string>? FunctionModels { get; set; }

        /// <summary>
        /// 短期记忆最大消息条数（主人格默认值）
        /// </summary>
        public int MaxMessages { get; set; } = 50;

        /// <summary>子人格列表</summary>
        public List<SubPersonality> Personalities { get; set; } = new();

        /// <summary>
        /// 桌宠显示模式："Gif"（2D 动图）或 "Vrm"（3D 模型）
        /// </summary>
        public string DisplayMode { get; set; } = "Gif";
    }

    /// <summary>
    /// 子人格定义
    /// 仅包含描述和权重，模型配置统一在主人格中管理
    /// </summary>
    public class SubPersonality
    {
        /// <summary>子人格名称（如：温柔、毒舌、活泼）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>子人格描述/行为规则</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 子人格切换概率权重（0-100）
        /// 所有子人格的权重和必须为100，否则人格切换机制禁用
        /// </summary>
        public int Weight { get; set; } = 0;
    }
}
