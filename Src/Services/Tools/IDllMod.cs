namespace MochiBot.Src.Services.Tool
{
    /// <summary>
    /// DLLMOD 插件接口
    /// 所有 DLL 插件需实现此接口以注册为工具
    /// </summary>
    public interface IDllMod
    {
        /// <summary>插件名称（唯一标识）</summary>
        string Name { get; }

        /// <summary>插件描述</summary>
        string Description { get; }

        /// <summary>执行插件功能</summary>
        /// <param name="parameters">传入参数（JSON格式）</param>
        /// <returns>执行结果（JSON格式）</returns>
        Task<string> ExecuteAsync(string parameters);
    }
}
