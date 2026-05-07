using System.IO;
using System.Reflection;
using System.Text.Json;

namespace MochiBot.Src.Services.Tool
{
    /// <summary>
    /// DLLMOD 加载器实现
    /// 从指定目录加载 DLL 插件，支持热更新
    /// </summary>
    public class DllModLoader : IDllModLoader
    {
        private readonly Dictionary<string, IDllMod> _mods = new();
        private readonly Lock _lock = new();
        private string? _modDirectory;

        public Task<List<IDllMod>> LoadModsAsync(string modDirectory)
        {
            _modDirectory = modDirectory;

            if (!Directory.Exists(modDirectory))
            {
                Directory.CreateDirectory(modDirectory);
                return Task.FromResult(new List<IDllMod>());
            }

            var mods = new List<IDllMod>();
            var dllFiles = Directory.GetFiles(modDirectory, "*.dll");

            lock (_lock)
            {
                _mods.Clear();

                foreach (var file in dllFiles)
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(file);
                        var modTypes = assembly.GetTypes()
                            .Where(t => typeof(IDllMod).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                        foreach (var modType in modTypes)
                        {
                            if (Activator.CreateInstance(modType) is IDllMod mod)
                            {
                                _mods[mod.Name] = mod;
                                mods.Add(mod);
                                System.Diagnostics.Debug.WriteLine($"[DllModLoader] 加载插件成功: {mod.Name} ({file})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DllModLoader] 加载插件失败: {file} - {ex.Message}");
                    }
                }
            }

            return Task.FromResult(mods);
        }

        public IDllMod GetMod(string name)
        {
            lock (_lock)
            {
                if (_mods.TryGetValue(name, out var mod))
                    return mod;
                throw new KeyNotFoundException($"DLLMOD '{name}' 未找到");
            }
        }

        public async Task<string> ExecuteModAsync(string modName, string parameters = "")
        {
            var mod = GetMod(modName);
            return await mod.ExecuteAsync(parameters);
        }

        public List<IDllMod> GetLoadedMods()
        {
            lock (_lock)
            {
                return new List<IDllMod>(_mods.Values);
            }
        }

        public async Task ReloadModsAsync()
        {
            if (_modDirectory != null)
            {
                await LoadModsAsync(_modDirectory);
            }
        }
    }
}
