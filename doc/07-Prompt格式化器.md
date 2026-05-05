# Prompt格式化器 (PromptFormatter)

## 模块概述

一个通用的模板格式化工具，接收模板字符串和变量字典，将模板中的占位符替换为实际值。其他模块自己管理模板字符串，通过 PromptFormatter 实例化并填充变量。

## 核心设计

```csharp
public class PromptFormatter
{
    private readonly string _template;

    public PromptFormatter(string template)
    {
        _template = template;
    }

    public string Format(Dictionary<string, string> variables)
    {
        var result = _template;
        foreach (var kv in variables)
        {
            result = result.Replace($"{{{kv.Key}}}", kv.Value);
        }
        return result;
    }
}
```

## 使用示例

```csharp
// 其他模块自己管理模板字符串
var greetTemplate = new PromptFormatter("你在问好{username}");
string prompt = greetTemplate.Format(new Dictionary<string, string>
{
    { "username", "test" }
});
// prompt = "你在问好test"

// 系统提示词模板
var systemPrompt = new PromptFormatter(@"
你是一个名叫{Name}的AI女友，你的性格是{Personality}。
【当前情绪】{CurrentMood}
【行为规则】
1. 每次回复要体现当前情绪状态
2. 回复长度控制在50字以内
3. 根据性格使用对应的语气和称呼
");

// 碎碎念模板（由 AutoEventService 管理）
var murmurPrompt = new PromptFormatter(@"
你现在是{Name}，性格{Personality}。
请说一句温暖的、无特定目的的唠叨。
当前情绪：{CurrentMood}
");

// 夸奖模板（由 ToolService 管理）
var complimentPrompt = new PromptFormatter(@"
你现在是{Name}，性格{Personality}。
请说一句夸奖用户的话。
");
```

## 接口定义

```csharp
/// <summary>
/// Prompt格式化器接口
/// </summary>
public interface IPromptFormatter
{
    /// <summary>使用变量字典填充模板，返回格式化后的字符串</summary>
    /// <param name="variables">变量名到值的映射</param>
    string Format(Dictionary<string, string> variables);
}
```

## 模板语法

模板中使用 `{变量名}` 作为占位符，Format 时通过变量字典替换。

示例模板：

```txt
你在问好{username}
```

变量字典：

```csharp
{ "username", "test" }
```

结果：

```txt
你在问好test
```

## 依赖关系

- **依赖**: 无（纯字符串替换工具）
- **被依赖**: 所有需要构建 prompt 的模块（Agent、AutoEventService、ToolService 等）

## 单元测试

### 测试要点

| 测试用例 | 预期结果 |
| ---------- | ---------- |
| 单个变量替换 | Format({"username":"test"}) 返回 "你在问好test" |
| 多个变量替换 | Format({"Name":"小可爱","Mood":"开心"}) 正确替换所有占位符 |
| 变量不存在时保留原样 | 未提供的变量占位符保持 {变量名} 不变 |
| 空变量字典 | 返回原始模板字符串 |
| 空模板 | 返回空字符串 |
| 多次 Format 调用互不影响 | 每次调用独立替换，不修改内部状态 |

### 测试方法

```csharp
[Fact]
public void Format_SingleVariable_ShouldReplace()
{
    var formatter = new PromptFormatter("你在问好{username}");
    var result = formatter.Format(new Dictionary<string, string> { { "username", "test" } });
    Assert.Equal("你在问好test", result);
}

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

[Fact]
public void Format_MissingVariable_ShouldKeepPlaceholder()
{
    var formatter = new PromptFormatter("你好{Name}");
    var result = formatter.Format(new Dictionary<string, string>());
    Assert.Equal("你好{Name}", result);
}

[Fact]
public void Format_EmptyTemplate_ShouldReturnEmpty()
{
    var formatter = new PromptFormatter("");
    var result = formatter.Format(new Dictionary<string, string> { { "k", "v" } });
    Assert.Equal("", result);
}

[Fact]
public void Format_MultipleCalls_ShouldBeIndependent()
{
    var formatter = new PromptFormatter("{key}");
    var r1 = formatter.Format(new Dictionary<string, string> { { "key", "v1" } });
    var r2 = formatter.Format(new Dictionary<string, string> { { "key", "v2" } });
    Assert.Equal("v1", r1);
    Assert.Equal("v2", r2);
}
```

## 设计要点

1. **通用性**：不限定具体模板内容，任何模块都可以使用
2. **无状态**：每个 PromptFormatter 实例只关联一个模板，Format 调用不修改内部状态
3. **轻量**：纯字符串替换，无外部依赖
4. **模块自治**：各模块自己管理模板字符串，PromptFormatter 只负责格式化
