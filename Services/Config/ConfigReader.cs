using System.Text.Json;
using System.Text.RegularExpressions;
using catgirlwindow.Services.Config.Models;

namespace catgirlwindow.Services.Config;

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
        _personalitiesDir = personalitiesDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Personalities");
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
    /// </summary>
    public void Reload()
    {
        if (!File.Exists(_configPath))
            throw new FileNotFoundException($"Configuration file '{_configPath}' not found.");

        var json = File.ReadAllText(_configPath);
        _cachedDoc = JsonDocument.Parse(json);
        _cachedAppConfig = null;
        _cachedModuleSettings = null;
        _cachedPersonality = null;

        // 初始化日志文件写入器
        InitLogFileWriter();

        _logger.Info("配置已重新加载");
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
            var json = File.ReadAllText(filePath);
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

        var sub = personality.Personalities.FirstOrDefault(p =>
            p.Name.Equals(subPersonalityName, StringComparison.OrdinalIgnoreCase));

        if (sub == null)
            return (new List<string>(), null);

        return (sub.ChatModels, sub.VisionModels);
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
                providers[prop.Name] = p;
            }
        }

        // 解析 AppSettings
        var settings = new AppSettings();
        if (root.TryGetProperty("AppSettings", out var appSettingsElement))
        {
            settings.ActivePersonality = GetStringProperty(appSettingsElement, "ActivePersonality", "default");
            settings.EnableStructuredResponse = GetBoolProperty(appSettingsElement, "EnableStructuredResponse", true);
            settings.MaxActionsPerResponse = GetIntProperty(appSettingsElement, "MaxActionsPerResponse", 5);
            settings.EnableMidTermMemoryOnChat = GetBoolProperty(appSettingsElement, "EnableMidTermMemoryOnChat", true);
            settings.EnableLongTermRecall = GetBoolProperty(appSettingsElement, "EnableLongTermRecall", true);
            settings.LogLevel = GetStringProperty(appSettingsElement, "LogLevel", "Info");
            settings.LogToFile = GetBoolProperty(appSettingsElement, "LogToFile", true);
            settings.LogToConsole = GetBoolProperty(appSettingsElement, "LogToConsole", true);
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
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Logs");
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cachedDoc?.Dispose();
        _logFileWriter?.Dispose();
    }
}
