using OpenAI.Chat;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;

namespace MochiBot.Src.Services
{
    public class LlmClient
    {
        private readonly IConfigReader _configReader;
        private readonly Dictionary<string, ProviderConfig> _providers;
        private ChatClient _chatClient;

        public LlmClient(string provider,string model,IConfigReader configReader)
        {
            _configReader = configReader;
            _providers = _configReader.GetAllProviders();;

            if (!_providers.TryGetValue(provider, out var config))
            {throw new ArgumentException($"Provider '{provider}' not found in configuration.");}

            var client = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(config.ApiKey), new OpenAI.OpenAIClientOptions
            {Endpoint = new Uri(config.BaseUrl)});
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
            var completion = await _chatClient.CompleteChatAsync(messages);
            return completion.Value.Content[0].Text;
        }

        public IEnumerable<string> GetAvailableProviders()
        {
            return _providers.Keys;
        }
    }
}
