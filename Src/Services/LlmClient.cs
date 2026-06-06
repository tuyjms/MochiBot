using System.Net.Http;
using OpenAI.Chat;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;

namespace MochiBot.Src.Services
{
    public class LlmClient
    {
        private readonly IConfigReader _configReader;
        private readonly Dictionary<string, ProviderConfig> _providers;
        private readonly ProviderConfig _currentProviderConfig;
        private ChatClient _chatClient;

        /// <summary>该模型是否支持视觉输入（图片）</summary>
        public bool SupportsVision { get; }

        public LlmClient(string provider,string model,IConfigReader configReader)
        {
            _configReader = configReader;
            _providers = _configReader.GetAllProviders();;

            if (!_providers.TryGetValue(provider, out var config))
            {throw new ArgumentException($"Provider '{provider}' not found in configuration.");}

            _currentProviderConfig = config;

            // 从模型注册表读取视觉标识
            SupportsVision = config.Models?.FirstOrDefault(m => m.Name == model)?.SupportsVision ?? false;

            var client = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(config.ApiKey), new OpenAI.OpenAIClientOptions
            {Endpoint = new Uri(config.BaseUrl), NetworkTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds)});
            _chatClient = client.GetChatClient(model);
        }

        public virtual async Task<string> SendChatAsync(string prompt)
        {
            var messages = new List<ChatMessage>
            {
                new UserChatMessage(prompt)
            };
            return await SendChatAsync(messages);
        }

        public virtual async Task<string> SendChatAsync(List<ChatMessage> messages)
        {
            var maxRetries = _currentProviderConfig.MaxRetries;
            var baseDelay = _currentProviderConfig.RetryDelayMs;

            Exception? lastException = null;
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        // 指数退避：baseDelay * 2^(attempt-1)，加随机抖动
                        var delay = baseDelay * (1 << (attempt - 1)) + Random.Shared.Next(0, 500);
                        _configReader.Logger.Warn($"[LlmClient] 第 {attempt} 次重试，等待 {delay}ms...");
                        await Task.Delay(delay);
                    }

                    return await CallLlmAsync(messages);
                }
                catch (Exception ex) when (IsTransient(ex))
                {
                    lastException = ex;
                    _configReader.Logger.Warn($"[LlmClient] 请求失败 (尝试 {attempt + 1}/{maxRetries + 1}): {ex.Message}");
                }
            }

            throw new InvalidOperationException(
                $"[LlmClient] {maxRetries + 1} 次尝试均失败，最后错误: {lastException?.Message}", lastException);
        }

        /// <summary>发送包含图片的多模态消息（供 VisionService 调用）</summary>
        public virtual async Task<string> SendVisionAsync(string textPrompt, byte[] imageBytes)
        {
            var parts = new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(textPrompt),
                ChatMessageContentPart.CreateImagePart(System.BinaryData.FromBytes(imageBytes), "image/png")
            };
            var messages = new List<ChatMessage> { new UserChatMessage(parts) };
            return await CallLlmAsync(messages);
        }

        /// <summary>发送包含图片的多模态消息列表（供 Agent 直传截图使用）</summary>
        /// <remarks>将最后一条 UserChatMessage 替换为文本+图片的多模态版本，其余消息原样保留</remarks>
        public virtual async Task<string> SendChatWithImageAsync(List<ChatMessage> messages, byte[] imageBytes)
        {
            var finalMessages = new List<ChatMessage>();
            for (int i = 0; i < messages.Count; i++)
            {
                if (i == messages.Count - 1 && messages[i] is UserChatMessage userMsg)
                {
                    // 提取原始文本内容
                    var textContent = string.Join("", userMsg.Content
                        .Where(p => p.Kind == ChatMessageContentPartKind.Text)
                        .Select(p => p.Text));
                    // 构建多模态消息：文本 + 图片
                    var parts = new List<ChatMessageContentPart>
                    {
                        ChatMessageContentPart.CreateTextPart(textContent),
                        ChatMessageContentPart.CreateImagePart(System.BinaryData.FromBytes(imageBytes), "image/png")
                    };
                    finalMessages.Add(new UserChatMessage(parts));
                }
                else
                {
                    finalMessages.Add(messages[i]);
                }
            }
            return await CallLlmAsync(finalMessages);
        }

        /// <summary>实际的 LLM 调用（可被子类重写以模拟网络行为）</summary>
        protected virtual async Task<string> CallLlmAsync(List<ChatMessage> messages)
        {
            var completion = await _chatClient.CompleteChatAsync(messages);
            return completion.Value.Content[0].Text;
        }

        /// <summary>判断异常是否为瞬态错误（值得重试）</summary>
        private static bool IsTransient(Exception ex)
        {
            // 网络异常
            if (ex is HttpRequestException) return true;
            if (ex is TaskCanceledException) return true; // 超时
            if (ex is System.Net.Sockets.SocketException) return true;

            // OpenAI SDK 的 ClientResultException：5xx 或 429（限流）值得重试
            if (ex is System.ClientModel.ClientResultException clientEx)
            {
                var status = clientEx.Status;
                return status >= 500 || status == 429;
            }

            return false;
        }

        public IEnumerable<string> GetAvailableProviders()
        {
            return _providers.Keys;
        }
    }
}
