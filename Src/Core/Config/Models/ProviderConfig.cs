namespace MochiBot.Src.Core.Config.Models
{
    /// <summary>
    /// LLM提供商配置
    /// </summary>
    public class ProviderConfig
    {
        /// <summary>API密钥</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>API基础地址</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>上下文极限（token数），超过此值需截断或压缩，防止调用时溢出</summary>
        public int ContextLimit { get; set; } = 4096;
    }
}
