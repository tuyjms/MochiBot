namespace MochiBot.Src.Core.Config.Models
{
    /// <summary>
    /// LLM提供商配置
    /// </summary>
    public class ProviderConfig
    {
        /// <summary>默认提供商名称（首次生成配置时使用）</summary>
        public const string DefaultProviderName = "LocalLMStudio";

        /// <summary>默认 API 密钥</summary>
        public const string DefaultApiKey = "not-needed";

        /// <summary>默认 API 基础地址</summary>
        public const string DefaultBaseUrl = "http://localhost:1234/v1";

        /// <summary>API密钥</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>API基础地址</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>上下文极限（token数），超过此值需截断或压缩，防止调用时溢出</summary>
        public int ContextLimit { get; set; } = 4096;
    }
}
