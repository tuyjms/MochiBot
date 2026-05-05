namespace catgirlwindow.Plugins;

/// <summary>
/// JS插件加载器接口
/// </summary>
public interface IJsPluginLoader
{
    /// <summary>从指定目录加载所有插件</summary>
    /// <param name="pluginDirectory">插件目录路径</param>
    Task<List<IJsPlugin>> LoadPluginsAsync(string pluginDirectory);

    /// <summary>根据名称获取已加载的插件</summary>
    /// <param name="name">插件名称</param>
    IJsPlugin GetPlugin(string name);

    /// <summary>执行指定名称的插件</summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="parameters">参数（JSON字符串）</param>
    Task<string> ExecutePluginAsync(string pluginName, string parameters = "");

    /// <summary>获取所有已加载的插件列表</summary>
    List<IJsPlugin> GetLoadedPlugins();

    /// <summary>重新加载所有插件（热更新）</summary>
    Task ReloadPluginsAsync();
}
