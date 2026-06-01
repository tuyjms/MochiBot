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

        public LlmClient(string provider,string model,IConfigReader configReader)
        {
            _configReader = configReader;
            _providers = _configReader.GetAllProviders();;

            if (!_providers.TryGetValue(provider, out var config))
            {throw new ArgumentException($"Provider '{provider}' not found in configuration.");}

            _currentProviderConfig = config;

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
