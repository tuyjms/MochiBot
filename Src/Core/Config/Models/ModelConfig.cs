namespace MochiBot.Src.Core.Config.Models
{
    /// <summary>
    /// 单个 LLM 模型配置
    /// 注册在 ProviderConfig.Models 列表中，供 LlmClient 和 VisionService 查找
    /// </summary>
    public class ModelConfig
    {
        /// <summary>模型名称（对应 API 的 model 参数，如 "deepseek-chat"）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>是否支持视觉输入（图片）</summary>
        public bool SupportsVision { get; set; } = false;
    }
}
