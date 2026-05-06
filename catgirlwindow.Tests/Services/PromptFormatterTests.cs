using catgirlwindow.Src.Agent;
namespace catgirlwindow.Tests;

public class PromptFormatterTests
{
    // ========== 单个变量替换 ==========

    [Fact]
    public void Format_SingleVariable_ShouldReplace()
    {
        var formatter = new PromptFormatter("你在问好{username}");
        var result = formatter.Format(new Dictionary<string, string> { { "username", "test" } });
        Assert.Equal("你在问好test", result);
    }

    // ========== 多个变量替换 ==========

    [Fact]
    public void Format_MultipleVariables_ShouldReplaceAll()
    {
        var formatter = new PromptFormatter("你好{Name}，今天{Weather}");
        var result = formatter.Format(new Dictionary<string, string>
        {
            { "Name", "小明" },
            { "Weather", "晴天" }
        });
        Assert.Equal("你好小明，今天晴天", result);
    }

    // ========== 变量不存在时保留原样 ==========

    [Fact]
    public void Format_MissingVariable_ShouldKeepPlaceholder()
    {
        var formatter = new PromptFormatter("你好{Name}");
        var result = formatter.Format(new Dictionary<string, string>());
        Assert.Equal("你好{Name}", result);
    }

    // ========== 空变量字典 ==========

    [Fact]
    public void Format_EmptyVariables_ShouldReturnTemplate()
    {
        var formatter = new PromptFormatter("你好{Name}");
        var result = formatter.Format(new Dictionary<string, string>());
        Assert.Equal("你好{Name}", result);
    }

    // ========== 空模板 ==========

    [Fact]
    public void Format_EmptyTemplate_ShouldReturnEmpty()
    {
        var formatter = new PromptFormatter("");
        var result = formatter.Format(new Dictionary<string, string> { { "k", "v" } });
        Assert.Equal("", result);
    }

    // ========== 多次 Format 调用互不影响 ==========

    [Fact]
    public void Format_MultipleCalls_ShouldBeIndependent()
    {
        var formatter = new PromptFormatter("{key}");
        var r1 = formatter.Format(new Dictionary<string, string> { { "key", "v1" } });
        var r2 = formatter.Format(new Dictionary<string, string> { { "key", "v2" } });
        Assert.Equal("v1", r1);
        Assert.Equal("v2", r2);
    }

    // ========== 部分变量替换 ==========

    [Fact]
    public void Format_PartialVariables_ShouldReplaceExistingAndKeepMissing()
    {
        var formatter = new PromptFormatter("你好{Name}，今天{Weather}");
        var result = formatter.Format(new Dictionary<string, string>
        {
            { "Name", "小明" }
        });
        Assert.Equal("你好小明，今天{Weather}", result);
    }

    // ========== 变量值包含花括号 ==========

    [Fact]
    public void Format_ValueWithBraces_ShouldHandleCorrectly()
    {
        var formatter = new PromptFormatter("{key}");
        var result = formatter.Format(new Dictionary<string, string> { { "key", "{value}" } });
        Assert.Equal("{value}", result);
    }

    // ========== 模板中无占位符 ==========

    [Fact]
    public void Format_NoPlaceholders_ShouldReturnTemplate()
    {
        var formatter = new PromptFormatter("纯文本模板");
        var result = formatter.Format(new Dictionary<string, string> { { "key", "value" } });
        Assert.Equal("纯文本模板", result);
    }

    // ========== 接口实现 ==========

    [Fact]
    public void Implements_IPromptFormatter()
    {
        var formatter = new PromptFormatter("test");
        Assert.IsAssignableFrom<IPromptFormatter>(formatter);
    }
}
