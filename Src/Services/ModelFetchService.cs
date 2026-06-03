using System.Net.Http;
using System.Text.Json;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 从 LLM 提供商的 /v1/models API 获取可用模型列表
    /// </summary>
    public class ModelFetchService
    {
        private readonly IConfigReader _configReader;

        public ModelFetchService(IConfigReader configReader)
        {
            _configReader = configReader;
        }

        /// <summary>获取指定提供商的可用模型列表</summary>
        public async Task<List<string>> FetchModelsAsync(string providerName)
        {
            var providerConfig = _configReader.GetProvider(providerName);
            if (providerConfig == null)
                return new List<string>();

            var baseUrl = providerConfig.BaseUrl.TrimEnd('/');
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {providerConfig.ApiKey}");
            var response = await http.GetAsync($"{baseUrl}/models");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                return data.EnumerateArray()
                    .Where(m => m.TryGetProperty("id", out _))
                    .Select(m => m.GetProperty("id").GetString()!)
                    .OrderBy(m => m)
                    .ToList();
            }
            return new List<string>();
        }
    }
}
