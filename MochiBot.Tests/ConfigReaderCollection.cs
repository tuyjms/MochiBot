using Xunit;

namespace MochiBot.Tests
{
    /// <summary>
    /// xUnit 测试集合定义：确保所有依赖 ConfigReader 单例的测试顺序执行
    /// </summary>
    [CollectionDefinition("ConfigReader")]
    public class ConfigReaderCollection { }
}
