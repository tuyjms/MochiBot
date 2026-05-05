using System.Text.Json;
using OpenAI.Chat;

namespace catgirlwindow;

public class LlmClient
{
    private readonly Dictionary<string, ProviderConfig> _providers;

    public LlmClient()
    {
        _providers = LoadProviders();
    }

    private static Dictionary<string, ProviderConfig> LoadProviders()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Configuration file 'appsettings.json' not found.");
        }

        var json = File.ReadAllText(configPath);
        using var document = JsonDocument.Parse(json);
        var providers = new Dictionary<string, ProviderConfig>();

        if (document.RootElement.TryGetProperty("Providers", out var providersElement))
        {
            foreach (var property in providersElement.EnumerateObject())
            {
                var providerName = property.Name;
                var configElement = property.Value;
                var apiKey = configElement.GetProperty("ApiKey").GetString() ?? "";
                var baseUrl = configElement.GetProperty("BaseUrl").GetString() ?? "";
                providers[providerName] = new ProviderConfig(apiKey, baseUrl);
            }
        }

        return providers;
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

public record ProviderConfig(string ApiKey, string BaseUrl);
