namespace MochiBot.Src.Services.Tool
{
    /// <summary>
    /// DLLMOD 加载器接口
    /// 从指定目录加载 DLL 插件，支持热更新
    /// </summary>
    public interface IDllModLoader
    {
        /// <summary>从指定目录加载所有DLL插件</summary>
        /// <param name="modDirectory">插件目录路径</param>
        Task<List<IDllMod>> LoadModsAsync(string modDirectory);

        /// <summary>根据名称获取已加载的插件</summary>
        /// <param name="name">插件名称</param>
        IDllMod GetMod(string name);

        /// <summary>执行指定名称的插件</summary>
        /// <param name="modName">插件名称</param>
        /// <param name="parameters">参数（JSON字符串）</param>
        Task<string> ExecuteModAsync(string modName, string parameters = "");

        /// <summary>获取所有已加载的插件列表</summary>
        List<IDllMod> GetLoadedMods();

        /// <summary>重新加载所有插件（热更新）</summary>
        Task ReloadModsAsync();
    }
}
