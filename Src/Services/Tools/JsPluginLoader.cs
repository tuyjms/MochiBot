using System.IO;
using System.Text.Json;

namespace MochiBot.Src.Services.Tool
{
    /// <summary>
    /// JS插件加载器实现
    /// 从指定目录加载JS插件，支持热更新
    /// </summary>
    public class JsPluginLoader : IJsPluginLoader
    {
        private readonly Dictionary<string, IJsPlugin> _plugins = new();
        private readonly Lock _lock = new();
        private string? _pluginDirectory;

        public Task<List<IJsPlugin>> LoadPluginsAsync(string pluginDirectory)
        {
            _pluginDirectory = pluginDirectory;

            if (!Directory.Exists(pluginDirectory))
            {
                Directory.CreateDirectory(pluginDirectory);
                return Task.FromResult(new List<IJsPlugin>());
            }

            var plugins = new List<IJsPlugin>();
            var jsFiles = Directory.GetFiles(pluginDirectory, "*.js");

            lock (_lock)
            {
                _plugins.Clear();

                foreach (var file in jsFiles)
                {
                    try
                    {
                        var plugin = new JsPluginWrapper(file);
                        _plugins[plugin.Name] = plugin;
                        plugins.Add(plugin);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[JsPluginLoader] 加载插件失败: {file} - {ex.Message}");
                    }
                }
            }

            return Task.FromResult(plugins);
        }

        public IJsPlugin GetPlugin(string name)
        {
            lock (_lock)
            {
                if (_plugins.TryGetValue(name, out var plugin))
                    return plugin;
                throw new KeyNotFoundException($"插件 '{name}' 未找到");
            }
        }

        public async Task<string> ExecutePluginAsync(string pluginName, string parameters = "")
        {
            var plugin = GetPlugin(pluginName);
            return await plugin.ExecuteAsync(parameters);
        }

        public List<IJsPlugin> GetLoadedPlugins()
        {
            lock (_lock)
            {
                return new List<IJsPlugin>(_plugins.Values);
            }
        }

        public async Task ReloadPluginsAsync()
        {
            if (_pluginDirectory != null)
            {
                await LoadPluginsAsync(_pluginDirectory);
            }
        }

        /// <summary>
        /// JS插件包装器
        /// 从JS文件加载，解析元数据，执行时返回占位结果
        /// </summary>
        private class JsPluginWrapper : IJsPlugin
        {
            public string Name { get; }
            public string Description { get; }
            public string Icon { get; }

            private readonly string _filePath;
            private readonly string _scriptContent;

            public JsPluginWrapper(string filePath)
            {
                _filePath = filePath;
                _scriptContent = File.ReadAllText(filePath);

                // 从文件名提取名称
                Name = Path.GetFileNameWithoutExtension(filePath);

                // 默认描述和图标
                Description = $"JS插件: {Name}";
                Icon = "📦";

                // 解析脚本头部的注释元数据
                var lines = _scriptContent.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("// @description"))
                    {
                        Description = trimmed.Replace("// @description", "").Trim();
                    }
                    else if (trimmed.StartsWith("// @icon"))
                    {
                        Icon = trimmed.Replace("// @icon", "").Trim();
                    }
                }
            }

            public Task<string> ExecuteAsync(string parameters)
            {
                // 简化实现：返回占位结果
                // 实际实现需要使用 JavaScriptEngineSwitcher 或 ClearScript 执行JS
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    plugin = Name,
                    status = "executed",
                    message = $"插件 '{Name}' 已执行（JS引擎暂未集成）"
                }));
            }
        }
    }
}
