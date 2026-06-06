using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 视觉转文字服务
    /// 将截图交给 VisionModel 识别，返回文字描述
    /// </summary>
    public class VisionService
    {
        private readonly LlmClient? _visionLlmClient;
        private readonly IConfigReader _configReader;
        private readonly bool _isAvailable;

        public VisionService(IConfigReader configReader)
        {
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));

            try
            {
                var personality = configReader.GetActivePersonality();
                if (personality == null)
                {
                    _isAvailable = false;
                    configReader.Logger.Warn("[VisionService] 未找到激活人格，视觉功能不可用");
                    return;
                }

                // VisionModels 为空时 fallback 到 ChatModels 的第一个
                string? modelFullName = null;
                if (personality.VisionModels != null && personality.VisionModels.Count > 0)
                {
                    modelFullName = personality.VisionModels[0];
                }
                else if (personality.ChatModels != null && personality.ChatModels.Count > 0)
                {
                    modelFullName = personality.ChatModels[0];
                    configReader.Logger.Info($"[VisionService] VisionModels 未配置，fallback 到主聊天模型: {modelFullName}");
                }

                if (string.IsNullOrEmpty(modelFullName))
                {
                    _isAvailable = false;
                    configReader.Logger.Warn("[VisionService] 无可用模型，视觉功能不可用");
                    return;
                }

                var (provider, model) = ParseModelName(modelFullName);
                var llmClient = new LlmClient(provider, model, configReader);

                // 检查模型是否支持视觉输入
                if (!llmClient.SupportsVision)
                {
                    _isAvailable = false;
                    configReader.Logger.Warn($"[VisionService] 模型 {provider}/{model} 不支持视觉输入（SupportsVision=false）");
                    return;
                }

                _visionLlmClient = llmClient;
                _isAvailable = true;
                configReader.Logger.Info($"[VisionService] 已初始化视觉模型: {provider}/{model}");
            }
            catch (Exception ex)
            {
                _isAvailable = false;
                configReader.Logger.Error("[VisionService] 初始化失败，视觉功能不可用", ex);
            }
        }

        /// <summary>截取屏幕并用 VisionModel 生成文字描述，失败返回 null</summary>
        public async Task<string?> TryDescribeScreenAsync()
        {
            if (!_isAvailable || _visionLlmClient == null)
                return null;

            try
            {
                // 截屏（ScreenshotService 内部检查 consent）
                var screenshot = ScreenshotService.CaptureScreen(_configReader);
                if (screenshot == null || screenshot.Length == 0)
                    return null;

                // 调用 VisionModel
                var description = await _visionLlmClient.SendVisionAsync(
                    "请用中文简洁描述这张截图的内容，重点关注用户正在做什么。不要输出多余内容，只输出描述。",
                    screenshot);

                if (!string.IsNullOrWhiteSpace(description))
                {
                    _configReader.Logger.Debug($"[VisionService] 截图描述: {description}");
                    return description.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                _configReader.Logger.Warn($"[VisionService] 视觉识别失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>从模型名中提取提供商（格式："{提供商}/{模型名}"）</summary>
        private static (string provider, string model) ParseModelName(string modelFullName)
        {
            if (string.IsNullOrEmpty(modelFullName) || modelFullName == "default")
                return (ProviderConfig.DefaultProviderName, "default");

            var parts = modelFullName.Split('/', 2);
            if (parts.Length == 2)
                return (parts[0], parts[1]);

            return (ProviderConfig.DefaultProviderName, modelFullName);
        }
    }
}
