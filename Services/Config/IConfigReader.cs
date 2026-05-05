using catgirlwindow.Services.Config.Models;

namespace catgirlwindow.Services.Config;

/// <summary>
/// 配置读取器接口
/// </summary>
public interface IConfigReader
{
    // ========== Logger ==========

    /// <summary>获取日志记录器实例</summary>
    ILogger Logger { get; }


    // ========== LLM提供商配置 ==========

    /// <summary>获取所有LLM提供商配置</summary>
    Dictionary<string, Models.ProviderConfig> GetAllProviders();

    /// <summary>获取指定提供商的配置</summary>
    Models.ProviderConfig? GetProvider(string providerName);

    /// <summary>获取所有可用的提供商名称列表</summary>
    IEnumerable<string> GetAvailableProviders();


    // ========== 应用级配置 ==========

    /// <summary>获取应用级配置</summary>
    AppSettings GetAppSettings();


    // ========== 模块参数配置 ==========

    /// <summary>获取模块参数配置</summary>
    ModuleSettings GetModuleSettings();

    /// <summary>获取指定模块的单个参数值</summary>
    T GetModuleParam<T>(string paramName, T defaultValue);


    // ========== 人格配置 ==========

    /// <summary>获取当前激活的人格配置</summary>
    PersonalityConfig? GetActivePersonality();

    /// <summary>根据人物名称加载人格配置</summary>
    PersonalityConfig? LoadPersonality(string personalityName);

    /// <summary>获取所有可用的人格名称列表</summary>
    List<string> GetAvailablePersonalities();

    /// <summary>获取指定子人格的LLM模型配置</summary>
    /// <param name="subPersonalityName">子人格名称</param>
    /// <returns>(chatModels, visionModels) 元组，visionModels 可能为 null</returns>
    (List<string> chatModels, List<string>? visionModels) GetPersonalityModels(string subPersonalityName);


    // ========== 配置重载 ==========

    /// <summary>重新加载配置文件（热更新）</summary>
    void Reload();
}
