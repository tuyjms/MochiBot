namespace catgirlwindow.Src.Services
{
    /// <summary>
    /// JS插件接口（由JS脚本实现）
    /// </summary>
    public interface IJsPlugin
    {
        /// <summary>插件名称（唯一标识）</summary>
        string Name { get; }

        /// <summary>插件描述</summary>
        string Description { get; }

        /// <summary>图标标识（用于UI显示）</summary>
        string Icon { get; }

        /// <summary>执行插件功能</summary>
        /// <param name="parameters">传入参数（JSON格式）</param>
        /// <returns>执行结果（JSON格式）</returns>
        Task<string> ExecuteAsync(string parameters);
    }
}
