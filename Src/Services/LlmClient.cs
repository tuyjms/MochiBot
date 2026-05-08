using OpenAI.Chat;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;

namespace MochiBot.Src.Services
{
    public class LlmClient
    {
        private readonly IConfigReader _configReader;
        private readonly Dictionary<string, ProviderConfig> _providers;

        public LlmClient(IConfigReader configReader)
        {
            _configReader = configReader;
            _providers = _configReader.GetAllProviders();
        }

        public virtual async Task<string> SendChatAsync(string providerName, string model, string prompt)
        {
            var messages = new List<ChatMessage>
            {
                new UserChatMessage(prompt)
            };
            return await SendChatAsync(providerName, model, messages);
        }

        public virtual async Task<string> SendChatAsync(string providerName, string model, List<ChatMessage> messages)
        {
            if (!_providers.TryGetValue(providerName, out var config))
            {
                throw new ArgumentException($"Provider '{providerName}' not found in configuration.");
            }

            var client = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(config.ApiKey), new OpenAI.OpenAIClientOptions
            {
                Endpoint = new Uri(config.BaseUrl)
            });

            var chatClient = client.GetChatClient(model);

            var completion = await chatClient.CompleteChatAsync(messages);
            return completion.Value.Content[0].Text;
        }

        public IEnumerable<string> GetAvailableProviders()
        {
            return _providers.Keys;
        }
    }
}
