using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MochiBot.Src.Core;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Core.Events;
using static MochiBot.Src.Core.Constants;

namespace MochiBot.Src.Core.Config
{
    /// <summary>
    /// 配置读取器实现（单例模式）
    /// </summary>
    public class ConfigReader : IConfigReader, IDisposable
    {
        private static ConfigReader? _instance;
        private static readonly object _lock = new();

        private readonly string _configPath;
        private readonly string _personalitiesDir;
        private JsonDocument? _cachedDoc;
        private AppConfig? _cachedAppConfig;
        private ModuleSettings? _cachedModuleSettings;
        private PersonalityConfig? _cachedPersonality;
        private readonly ConsoleLogger _logger;
        private StreamWriter? _logFileWriter;
        private bool _disposed;

        /// <summary>
        /// 获取 ConfigReader 单例实例
        /// </summary>
        public static ConfigReader Instance
        {
            get
            {
                if (_instance == null)
                    throw new InvalidOperationException("ConfigReader has not been initialized. Call ConfigReader.Initialize() first.");
                return _instance;
            }
        }

        /// <summary>
        /// 获取日志记录器实例
        /// </summary>
        public ILogger Logger => _logger;

        private ConfigReader(string configPath, string? personalitiesDir = null)
        {
            _configPath = configPath;
            _personalitiesDir = personalitiesDir ?? Path.Combine(AppPaths.ResourcesDir, "Personalities");
            _logger = new ConsoleLogger(this);
            Reload();
        }

        /// <summary>
        /// 初始化 ConfigReader 单例
        /// </summary>
        /// <param name="configPath">appsettings.json 路径</param>
        /// <param name="personalitiesDir">人格配置文件目录（可选，默认为 Resources/Personalities/）</param>
        public static void Initialize(string configPath, string? personalitiesDir = null)
        {
            lock (_lock)
            {
                _instance?.Dispose();
                _instance = new ConfigReader(configPath, personalitiesDir);
            }
        }

        /// <summary>
        /// 重新加载配置文件
        /// 如果配置文件不存在，则生成一个包含默认配置的文件
        /// </summary>
        public void Reload()
        {
            if (!File.Exists(_configPath))
            {
                GenerateDefaultConfig();
            }

            var json = ReadAllTextWithRetry(_configPath);
            _cachedDoc = JsonDocument.Parse(json);
            _cachedAppConfig = null;
            _cachedModuleSettings = null;
            _cachedPersonality = null;

            // 初始化日志文件写入器
            InitLogFileWriter();

            _logger.Info("配置已重新加载");
        }

        /// <summary>
        /// 生成默认配置文件（使用模型类序列化，确保字段完整）
        /// </summary>
        private void GenerateDefaultConfig()
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var defaultMs = new ModuleSettings();

            var defaultConfig = new
            {
                Providers = new Dictionary<string, Models.ProviderConfig>
                {
                    [Models.ProviderConfig.DefaultProviderName] = new Models.ProviderConfig
                    {
                        ApiKey = Models.ProviderConfig.DefaultApiKey,
                        BaseUrl = Models.ProviderConfig.DefaultBaseUrl,
                    }
                },
                AppSettings = new AppSettings(),
                ModuleSettings = defaultMs,
                CronTasks = new List<CronTask>
                {
                    new() { Id = BuiltinTasks.IdMurmur, Name = BuiltinTasks.NameMurmur, TaskType = BuiltinTasks.Murmur, CronExpression = "*/30 * * * *", Parameters = defaultMs.AutoEvent_MurmurInterval.ToString(), Enabled = true },
                    new() { Id = BuiltinTasks.IdEyeRest, Name = BuiltinTasks.NameEyeRest, TaskType = BuiltinTasks.EyeRest, CronExpression = "*/5 * * * *", Parameters = defaultMs.AutoEvent_EyeRestInterval.ToString(), Enabled = true },
                    new() { Id = BuiltinTasks.IdLateNight, Name = BuiltinTasks.NameLateNight, TaskType = BuiltinTasks.LateNight, CronExpression = "0 23 * * *", Parameters = $"{defaultMs.AutoEvent_LateNightOffsetMin},{defaultMs.AutoEvent_LateNightOffsetMax}", Enabled = true },
                    new() { Id = BuiltinTasks.IdIdleCheck, Name = BuiltinTasks.NameIdleCheck, TaskType = BuiltinTasks.IdleCheck, CronExpression = "*/2 * * * *", Parameters = defaultMs.AutoEvent_IdleThreshold.ToString(), Enabled = true }
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(defaultConfig, options);
            WriteAllTextWithRetry(_configPath, json);
            _logger.Info($"配置文件不存在，已生成默认配置: {_configPath}");
        }

        // ========== LLM提供商配置 ==========

        public Dictionary<string, Models.ProviderConfig> GetAllProviders()
        {
            EnsureLoaded();
            return _cachedAppConfig!.Providers;
        }

        public Models.ProviderConfig? GetProvider(string providerName)
        {
            EnsureLoaded();
            return _cachedAppConfig!.Providers.TryGetValue(providerName, out var config) ? config : null;
        }

        public IEnumerable<string> GetAvailableProviders()
        {
            EnsureLoaded();
            return _cachedAppConfig!.Providers.Keys;
        }

        // ========== 应用级配置 ==========

        public AppSettings GetAppSettings()
        {
            EnsureLoaded();
            return _cachedAppConfig!.Settings;
        }

        // ========== 模块参数配置 ==========

        public ModuleSettings GetModuleSettings()
        {
            EnsureLoaded();
            return _cachedModuleSettings!;
        }

        public T GetModuleParam<T>(string paramName, T defaultValue)
        {
            EnsureLoaded();
            var settings = _cachedModuleSettings;
            if (settings == null) return defaultValue;

            var prop = typeof(ModuleSettings).GetProperty(paramName);
            if (prop == null) return defaultValue;

            var value = prop.GetValue(settings);
            if (value == null) return defaultValue;

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        // ========== 定时任务配置 ==========

        public List<CronTask> GetCronTasks()
        {
            EnsureLoaded();
            if (_cachedDoc == null) return new List<CronTask>();

            if (_cachedDoc.RootElement.TryGetProperty("CronTasks", out var cronTasksElement) &&
                cronTasksElement.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var tasks = JsonSerializer.Deserialize<List<CronTask>>(cronTasksElement.GetRawText(), options);
                    return tasks ?? new List<CronTask>();
                }
                catch
                {
                    return new List<CronTask>();
                }
            }
            return new List<CronTask>();
        }

        // ========== 人格配置 ==========

        public PersonalityConfig? GetActivePersonality()
        {
            EnsureLoaded();
            if (_cachedPersonality != null)
                return _cachedPersonality;

            var activeName = _cachedAppConfig?.Settings.ActivePersonality;
            if (string.IsNullOrEmpty(activeName) || activeName == "default")
                return null;

            _cachedPersonality = LoadPersonality(activeName);
            return _cachedPersonality;
        }

        public PersonalityConfig? LoadPersonality(string personalityName)
        {
            if (!IsValidPersonalityName(personalityName))
                return null;

            var fileName = $"{personalityName.ToLowerInvariant()}_person.json";
            var filePath = Path.Combine(_personalitiesDir, fileName);

            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = ReadAllTextWithRetry(filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = JsonSerializer.Deserialize<PersonalityConfig>(json, options);
                return config;
            }
            catch (Exception ex)
            {
                _logger.Error($"加载人格配置文件失败: {filePath}", ex);
                return null;
            }
        }

        public List<string> GetAvailablePersonalities()
        {
            if (!Directory.Exists(_personalitiesDir))
                return new List<string>();

            var files = Directory.GetFiles(_personalitiesDir, "*_person.json");
            var names = new List<string>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                // 去掉 _person 后缀
                if (fileName.EndsWith("_person", StringComparison.OrdinalIgnoreCase))
                {
                    var name = fileName[..^7];
                    names.Add(name);
                }
            }

            return names;
        }

        public (List<string> chatModels, List<string>? visionModels) GetPersonalityModels(string subPersonalityName)
        {
            var personality = GetActivePersonality();
            if (personality == null)
                return (new List<string>(), null);

            // 模型配置统一在主人格中管理
            return (personality.ChatModels, personality.VisionModels);
        }

        // ========== 配置写入 ==========

        /// <summary>
        /// 保存应用设置到 appsettings.json 并刷新缓存
        /// </summary>
        public void SaveAppSettings(AppSettings newSettings)
        {
            try
            {
                var json = ReadAllTextWithRetry(_configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var dict = new Dictionary<string, object?>();

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == "AppSettings")
                    {
                        dict["AppSettings"] = newSettings;
                    }
                    else
                    {
                        dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    }
                }

                var newJson = JsonSerializer.Serialize(dict, options);
                WriteAllTextWithRetry(_configPath, newJson);

                Reload();
                Logger.Info("[ConfigReader] 应用设置已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("[ConfigReader] 保存应用设置失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 保存激活人格名称到 appsettings.json
        /// </summary>
        public void SaveActivePersonality(string personalityName)
        {
            var settings = GetAppSettings();
            settings.ActivePersonality = personalityName;
            SaveAppSettings(settings);
        }

        /// <summary>
        /// 保存 LLM 提供商配置到 appsettings.json 并刷新缓存
        /// </summary>
        public void SaveProviders(Dictionary<string, Models.ProviderConfig> providers)
        {
            try
            {
                var json = ReadAllTextWithRetry(_configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var dict = new Dictionary<string, object?>();

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == "Providers")
                    {
                        dict["Providers"] = providers;
                    }
                    else
                    {
                        dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    }
                }

                var newJson = JsonSerializer.Serialize(dict, options);
                WriteAllTextWithRetry(_configPath, newJson);

                Reload();
                Logger.Info("[ConfigReader] 提供商配置已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("[ConfigReader] 保存提供商配置失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 保存模块参数配置到 appsettings.json 并刷新缓存
        /// </summary>
        public void SaveModuleSettings(ModuleSettings settings)
        {
            try
            {
                var json = ReadAllTextWithRetry(_configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var dict = new Dictionary<string, object?>();

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == "ModuleSettings")
                    {
                        dict["ModuleSettings"] = settings;
                    }
                    else
                    {
                        dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    }
                }

                var newJson = JsonSerializer.Serialize(dict, options);
                WriteAllTextWithRetry(_configPath, newJson);

                Reload();
                Logger.Info("[ConfigReader] 模块参数配置已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("[ConfigReader] 保存模块参数配置失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 保存定时任务配置到 appsettings.json 并刷新缓存
        /// </summary>
        public void SaveCronTasks(List<CronTask> tasks)
        {
            try
            {
                var json = ReadAllTextWithRetry(_configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var dict = new Dictionary<string, object?>();

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == "CronTasks")
                    {
                        dict["CronTasks"] = tasks;
                    }
                    else
                    {
                        dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                    }
                }

                var newJson = JsonSerializer.Serialize(dict, options);
                WriteAllTextWithRetry(_configPath, newJson);

                Reload();
                Logger.Info("[ConfigReader] 定时任务配置已保存");
            }
            catch (Exception ex)
            {
                Logger.Error("[ConfigReader] 保存定时任务配置失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 保存人格配置到对应的 *_person.json 文件
        /// </summary>
        public void SavePersonality(string personalityName, PersonalityConfig config)
        {
            if (!IsValidPersonalityName(personalityName))
            {
                Logger.Warn($"[ConfigReader] 无效的人格名称: {personalityName}");
                throw new ArgumentException($"无效的人格名称: {personalityName}", nameof(personalityName));
            }

            try
            {
                var fileName = $"{personalityName.ToLowerInvariant()}_person.json";
                var filePath = Path.Combine(_personalitiesDir, fileName);

                if (!Directory.Exists(_personalitiesDir))
                    Directory.CreateDirectory(_personalitiesDir);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config, options);
                WriteAllTextWithRetry(filePath, json);

                // 如果保存的是当前激活的人格，清除缓存以触发重新加载
                if (_cachedAppConfig?.Settings.ActivePersonality == personalityName)
                {
                    _cachedPersonality = null;
                }

                Logger.Info($"[ConfigReader] 人格配置已保存: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error("[ConfigReader] 保存人格配置失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 删除指定人格的 *_person.json 文件
        /// </summary>
        public void DeletePersonality(string personalityName)
        {
            if (!IsValidPersonalityName(personalityName))
            {
                Logger.Warn($"[ConfigReader] 无效的人格名称: {personalityName}");
                throw new ArgumentException($"无效的人格名称: {personalityName}", nameof(personalityName));
            }

            try
            {
                var fileName = $"{personalityName.ToLowerInvariant()}_person.json";
                var filePath = Path.Combine(_personalitiesDir, fileName);

                if (!File.Exists(filePath))
                {
                    Logger.Warn($"[ConfigReader] 人格配置文件不存在: {filePath}");
                    return;
                }

                File.Delete(filePath);

                // 如果删除的是当前激活的人格，清除缓存
                if (_cachedAppConfig?.Settings.ActivePersonality == personalityName)
                {
                    _cachedPersonality = null;
                }

                Logger.Info($"[ConfigReader] 人格配置已删除: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error("[ConfigReader] 删除人格配置失败", ex);
                throw;
            }
        }

        // ========== 人物名称合法性检查 ==========

        /// <summary>
        /// 检查人格名称是否合法
        /// </summary>
        public static bool IsValidPersonalityName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // 不能以数字开头
            if (char.IsDigit(name[0]))
                return false;

            // 只允许中文、英文字母、数字、下划线
            return Regex.IsMatch(name, @"^[\u4e00-\u9fa5a-zA-Z0-9_]+$");
        }

        // ========== 内部方法 ==========

        private void EnsureLoaded()
        {
            if (_cachedDoc == null)
                Reload();

            if (_cachedAppConfig == null)
                ParseAppConfig();
        }

        private void ParseAppConfig()
        {
            if (_cachedDoc == null) return;

            var root = _cachedDoc.RootElement;

            // 解析 Providers
            var providers = new Dictionary<string, Models.ProviderConfig>();
            if (root.TryGetProperty("Providers", out var providersElement))
            {
                foreach (var prop in providersElement.EnumerateObject())
                {
                    var p = new Models.ProviderConfig
                    {
                        ApiKey = prop.Value.TryGetProperty("ApiKey", out var apiKey) ? apiKey.GetString() ?? "" : "",
                        BaseUrl = prop.Value.TryGetProperty("BaseUrl", out var baseUrl) ? baseUrl.GetString() ?? "" : "",
                        ContextLimit = prop.Value.TryGetProperty("ContextLimit", out var ctxLimit) ? ctxLimit.GetInt32() : 4096
                    };

                    // 解析 Models 数组
                    if (prop.Value.TryGetProperty("Models", out var modelsElement) && modelsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var modelEl in modelsElement.EnumerateArray())
                        {
                            var mc = new Models.ModelConfig();
                            if (modelEl.TryGetProperty("Name", out var nameProp))
                                mc.Name = nameProp.GetString() ?? "";
                            if (modelEl.TryGetProperty("SupportsVision", out var svProp))
                                mc.SupportsVision = svProp.GetBoolean();
                            if (!string.IsNullOrWhiteSpace(mc.Name))
                                p.Models.Add(mc);
                        }
                    }

                    providers[prop.Name] = p;
                }
            }

            // 解析 AppSettings
            var settings = new AppSettings();
            if (root.TryGetProperty("AppSettings", out var appSettingsElement))
            {
                settings.UserName = GetStringProperty(appSettingsElement, "UserName", UserDefaults.DefaultUserName);
                settings.ActivePersonality = GetStringProperty(appSettingsElement, "ActivePersonality", "default");
                settings.EnableStructuredResponse = GetBoolProperty(appSettingsElement, "EnableStructuredResponse", true);
                settings.MaxActionsPerResponse = GetIntProperty(appSettingsElement, "MaxActionsPerResponse", 5);
                settings.EnableMidTermMemoryOnChat = GetBoolProperty(appSettingsElement, "EnableMidTermMemoryOnChat", true);
                settings.EnableLongTermRecall = GetBoolProperty(appSettingsElement, "EnableLongTermRecall", true);
                settings.LogLevel = GetStringProperty(appSettingsElement, "LogLevel", "Info");
                settings.LogToFile = GetBoolProperty(appSettingsElement, "LogToFile", true);
                settings.LogToConsole = GetBoolProperty(appSettingsElement, "LogToConsole", true);
                settings.CloseBehavior = GetStringProperty(appSettingsElement, "CloseBehavior", "Exit");
                if (appSettingsElement.TryGetProperty("PassthroughOpacity", out var opacityProp) && opacityProp.ValueKind == JsonValueKind.Number)
                    settings.PassthroughOpacity = opacityProp.GetDouble();
            }

            _cachedAppConfig = new AppConfig { Providers = providers, Settings = settings };

            // 解析 ModuleSettings
            _cachedModuleSettings = new ModuleSettings();
            if (root.TryGetProperty("ModuleSettings", out var moduleSettingsElement))
            {
                DeserializeModuleSettings(moduleSettingsElement);
            }
        }

        private void DeserializeModuleSettings(JsonElement element)
        {
            if (_cachedModuleSettings == null) return;

            var props = typeof(ModuleSettings).GetProperties();
            foreach (var prop in props)
            {
                if (!element.TryGetProperty(prop.Name, out var value))
                    continue;

                try
                {
                    if (prop.PropertyType == typeof(int))
                        prop.SetValue(_cachedModuleSettings, value.GetInt32());
                    else if (prop.PropertyType == typeof(double))
                        prop.SetValue(_cachedModuleSettings, value.GetDouble());
                    else if (prop.PropertyType == typeof(bool))
                        prop.SetValue(_cachedModuleSettings, value.GetBoolean());
                    else if (prop.PropertyType == typeof(string))
                        prop.SetValue(_cachedModuleSettings, value.GetString() ?? "");
                    else if (prop.PropertyType == typeof(List<string>))
                    {
                        var list = new List<string>();
                        foreach (var item in value.EnumerateArray())
                        {
                            list.Add(item.GetString() ?? "");
                        }
                        prop.SetValue(_cachedModuleSettings, list);
                    }
                }
                catch
                {
                    // 忽略类型转换失败，使用默认值
                }
            }
        }

        private void InitLogFileWriter()
        {
            try
            {
                var logDir = Path.Combine(AppPaths.ResourcesDir, "Logs");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                _logFileWriter = new StreamWriter(logFile, append: true) { AutoFlush = true };
            }
            catch
            {
                // 日志文件初始化失败不影响主流程
            }
        }

        internal void WriteLog(string level, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logLine = $"[{timestamp}] [{level}] {message}";

            // 控制台输出（使用 UTF-8 编码，确保中文正常显示）
            if (_cachedAppConfig?.Settings.LogToConsole ?? true)
            {
                try
                {
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                }
                catch { }
                Console.WriteLine(logLine);
            }

            // 文件输出
            if ((_cachedAppConfig?.Settings.LogToFile ?? true) && _logFileWriter != null)
            {
                try
                {
                    _logFileWriter.WriteLine(logLine);
                }
                catch
                {
                    // 忽略
                }
            }
        }

        // ========== JSON 辅助方法 ==========

        private static string GetStringProperty(JsonElement element, string name, string defaultValue)
        {
            return element.TryGetProperty(name, out var value) ? value.GetString() ?? defaultValue : defaultValue;
        }

        private static bool GetBoolProperty(JsonElement element, string name, bool defaultValue)
        {
            return element.TryGetProperty(name, out var value) ? value.GetBoolean() : defaultValue;
        }

        private static int GetIntProperty(JsonElement element, string name, int defaultValue)
        {
            return element.TryGetProperty(name, out var value) ? value.GetInt32() : defaultValue;
        }

        // ========== 内部类 ==========

        /// <summary>
        /// 应用配置根模型（内部使用）
        /// </summary>
        private class AppConfig
        {
            public Dictionary<string, Models.ProviderConfig> Providers { get; set; } = new();
            public AppSettings Settings { get; set; } = new();
        }

        /// <summary>
        /// 控制台日志记录器实现
        /// </summary>
        private class ConsoleLogger : ILogger
        {
            private readonly ConfigReader _reader;

            public ConsoleLogger(ConfigReader reader)
            {
                _reader = reader;
            }

            public void Debug(string message)
            {
                if (ShouldLog("Debug"))
                    _reader.WriteLog("DEBUG", message);
            }

            public void Info(string message)
            {
                if (ShouldLog("Info"))
                    _reader.WriteLog("INFO", message);
            }

            public void Warn(string message)
            {
                if (ShouldLog("Warn"))
                    _reader.WriteLog("WARN", message);
            }

            public void Error(string message, Exception? ex = null)
            {
                if (ShouldLog("Error"))
                {
                    var msg = ex != null ? $"{message} | {ex}" : message;
                    _reader.WriteLog("ERROR", msg);
                }
            }

            private bool ShouldLog(string level)
            {
                var configuredLevel = _reader._cachedAppConfig?.Settings.LogLevel ?? "Info";
                var levels = new[] { "Debug", "Info", "Warn", "Error" };
                var configuredIndex = Array.IndexOf(levels, configuredLevel);
                var currentIndex = Array.IndexOf(levels, level);
                return currentIndex >= configuredIndex;
            }
        }

        // ========== IDisposable ==========

        /// <summary>带重试的文件读取，解决多个 Save 方法快速连续操作同一文件时的 IOException</summary>
        private static string ReadAllTextWithRetry(string path, int retries = 3, int delayMs = 50)
        {
            for (int i = 0; i < retries; i++)
            {
                try { return File.ReadAllText(path); }
                catch (IOException) when (i < retries - 1) { Thread.Sleep(delayMs); }
            }
            return File.ReadAllText(path); // 最后一次不捕获，让异常抛出
        }

        /// <summary>带重试的文件写入</summary>
        private static void WriteAllTextWithRetry(string path, string content, int retries = 3, int delayMs = 50)
        {
            for (int i = 0; i < retries; i++)
            {
                try { File.WriteAllText(path, content); return; }
                catch (IOException) when (i < retries - 1) { Thread.Sleep(delayMs); }
            }
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cachedDoc?.Dispose();
            _logFileWriter?.Dispose();
        }
    }
}
