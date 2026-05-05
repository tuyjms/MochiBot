# Agent 核心协调层

## 模块概述

Agent 是系统的**大脑**和**中枢神经**，负责协调所有子模块。它有两种工作模式：

### 两种 LLM 调用模式

| 模式 | 场景 | 说明 |
| **对话模式** | 用户聊天、自动事件（碎碎念/关怀） | LLM 扮演"AI女友"角色，通过结构化响应控制工具/记忆/情绪 |
| **函数模式** | 记忆总结、关键词提取、重要度评估 | LLM 被当作纯函数调用，直接调用 LlmClient，不走人格层，保证稳定性 |

Agent 内部统一管理所有 LLM 调用（包括 API 密钥、重试、限流），但根据场景选择不同的调用方式。

## 核心接口

```csharp
public interface IAgent
{
    // ========== 对话模式（LLM扮演女友） ==========
    Task<string> ProcessUserInputAsync(string userMessage);
    Task<string> ProcessAutoEventAsync(string eventType, string? eventData = null);

    // ========== 函数模式（LLM作为纯函数） ==========
    Task<string> SummarizeMemoryAsync(string chatHistory);
    Task<(string kw1, string kw2, string kw3)> ExtractKeywordsAsync(string description);
    Task<int> EvaluateImportanceAsync(string content);

    // ========== 工具/插件调用 ==========
    Task<string> ProcessToolCallAsync(string toolName, string parameters);
    Task<string> ProcessPluginCallAsync(string pluginName, string parameters);

    // ========== 状态查询 ==========
    AgentStatus GetStatus();
}

public class AgentStatus
{
    public string CurrentMood { get; set; } = string.Empty;
    public int ShortTermMemoryCount { get; set; }
    public int MidTermMemoryCount { get; set; }
    public int LongTermMemoryCount { get; set; }
    public bool IsProcessing { get; set; }
    public string LastEvent { get; set; } = string.Empty;
}
```

## 对话模式：结构化响应协议

在对话模式下，LLM 的回复中除了自然语言文本外，可以附带结构化指令，Agent 解析后执行。

### Prompt 构建

Agent 使用 PromptFormatter 构建各类 prompt，各模块自己管理模板字符串。工具描述按三层结构动态注入：

```csharp
// Agent 内部维护的模板
private static readonly string SystemPromptTemplate = @"
你是一个名叫{Name}的AI女友，你的性格是{Personality}。
【当前情绪】{CurrentMood}

【可用工具】
{BaseTools}

【心情附加工具（当前情绪可用）】
{MoodTools}

【插件查询】
你可以调用 list_plugins 工具获取已加载的JS插件列表，然后通过 plugin_call 执行。

你可以通过返回 actions 数组来执行以下操作：
1. tool_call - 调用基础工具或心情附加工具
2. plugin_call - 调用已加载的JS插件（需先调用 list_plugins 获取列表）
3. mcp_call - 调用MCP服务器工具（需先调用 list_plugins 获取列表）
4. mood_change - 切换你的情绪（happy/sad/sleepy/touched/angry）
5. midterm_memory - 记录一条重要信息到中期记忆
6. animation - 播放动画（hug/pet_head/dance/cuddle）
";

// 构建时
var formatter = new PromptFormatter(SystemPromptTemplate);

// 第一层：基础工具（含 list_plugins）
var baseTools = _toolService.GetToolDefinitions();
var baseToolsDesc = string.Join("\n", baseTools.Select(t =>
    $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"
));

// 第三层：心情附加工具
var moodTools = _toolService.GetMoodBasedTools(_moodTracker.CurrentMood);
var moodToolsDesc = string.Join("\n", moodTools.Select(t =>
    $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"
));

var systemPrompt = formatter.Format(new Dictionary<string, string>
{
    { "Name", config.Name },
    { "Personality", config.Personality },
    { "CurrentMood", currentMood.ToString() },
    { "BaseTools", baseToolsDesc },
    { "MoodTools", moodToolsDesc }
});
```

Agent 在构建对话 prompt 时，会注入以下指令：

```txt
你可以通过返回 actions 数组来执行以下操作：
1. tool_call - 调用基础工具或心情附加工具
2. plugin_call - 调用已加载的JS插件（需先调用 list_plugins 获取列表）
3. mcp_call - 调用MCP服务器工具（需先调用 list_plugins 获取列表）
4. mood_change - 切换你的情绪（happy/sad/sleepy/touched/angry）
5. midterm_memory - 记录一条重要信息到中期记忆
6. animation - 播放动画（hug/pet_head/dance/cuddle）
```

### LLM 响应格式

```json
{
  "reply": "这是AI女友的自然语言回复内容...",
  "actions": [
    {"type": "tool_call", "name": "timer", "parameters": {"seconds": 300}},
    {"type": "mood_change", "mood": "happy"},
    {"type": "midterm_memory", "description": "用户提到下周要去面试", "importance": 70},
    {"type": "animation", "animation": "hug"}
  ]
}
```

## 函数模式：纯 LLM 调用

在函数模式下，LLM 被当作纯函数调用，不走人格层。这些调用使用专门的 system prompt，确保输出格式稳定。

### 记忆总结

```csharp
public async Task<string> SummarizeMemoryAsync(string chatHistory)
{
    var messages = new List<ChatMessage>
    {
        new() { Role = "system", Content = "你是一个对话摘要助手。请总结以下对话的核心内容，包括用户偏好、重要事件、待办事项。控制在200字以内。返回纯文本，不要加markdown格式。" },
        new() { Role = "user", Content = chatHistory }
    };
    return await _llmClient.SendChatAsync(messages);
}
```

### 关键词提取

```csharp
public async Task<(string, string, string)> ExtractKeywordsAsync(string description)
{
    var messages = new List<ChatMessage>
    {
        new() { Role = "system", Content = "从以下描述中提取3个关键词。优先主谓宾结构，没有主谓宾时用3个最重要的词。只返回JSON：{\"kw1\":\"...\",\"kw2\":\"...\",\"kw3\":\"...\"}" },
        new() { Role = "user", Content = description }
    };
    var result = await _llmClient.SendChatAsync(messages);
    return ParseKeywords(result);
}
```

### 重要度评估

```csharp
public async Task<int> EvaluateImportanceAsync(string content)
{
    var messages = new List<ChatMessage>
    {
        new() { Role = "system", Content = "评估以下内容的重要度，返回0-100的整数。只返回数字，不要其他文字。评估标准：个人偏好>60，重要事件>70，强烈情绪>80，长期需求>90。" },
        new() { Role = "user", Content = content }
    };
    var result = await _llmClient.SendChatAsync(messages);
    return int.TryParse(result, out var score) ? score : 30;
}
```

## 核心处理流程

### 1. 处理用户输入（对话模式）

1. 短期记忆.AddMessage("user", userMessage)
2. 构建完整 Prompt：PromptFormatter 构建 system prompt（含长期记忆检索）+ user context（含短期/中期记忆）+ Agent 指令
3. LlmClient.SendChatAsync(messages) 对话模式调用
4. 解析 LLM 响应：提取 reply 文本 + 解析 actions 数组
5. 遍历执行 actions：tool_call → ToolService、plugin_call → JsPluginLoader、mood_change → AgentMoodTracker、midterm_memory → MidTermMemory、animation → 2dRenderer
6. 短期记忆.AddMessage("assistant", reply)
7. 检查短期记忆是否溢出，如果溢出则按概率抽取，用函数模式 EvaluateImportanceAsync() 评估重要度后录入 MidTermMemory
8. AgentMoodTracker.UpdateMoodByEvent("Active")
9. 返回 reply 给 Form1 显示

### 2. 处理自动事件（对话模式）

1. AutoEventService 定时器到达，触发事件
2. Agent.ProcessAutoEventAsync(eventType, eventData) 被调用
3. 根据 eventType 构建对应 Prompt（murmur/eye_rest/late_night）
4. 注入 Agent 指令，调用 LlmClient.SendChatAsync() 对话模式
5. 解析 LLM 响应，执行 actions（可切换情绪等）
6. 短期记忆.AddMessage("assistant", reply)
7. 返回 reply → UI 显示气泡

### 3. 中期记忆定期关键词扫描（函数模式）

1. 定时任务触发（每30分钟）
2. 从短期记忆获取所有消息，统计词频找出TOP 10关键词
3. 用关键词查找相关对话段落
4. 对每个段落：函数模式 EvaluateImportanceAsync(content) 评估重要度
5. 如果重要度 > 阈值，录入中期记忆

### 4. 长期记忆提升（函数模式）

1. 定时任务触发（每60分钟）
2. 查询中期记忆中未提升且重要度 >= 60 的记录
3. 对每条记录：函数模式 ExtractKeywordsAsync(description) 提取关键词
4. 构建 LongTermMemoryEntry 录入 SQLite，标记中期记忆为已提升

## 自动事件与 Agent 的集成

AutoEventService 不再直接调用 LlmClient，而是通过 Agent 统一调度：

```csharp
public class AutoEventService : IAutoEventService
{
    private readonly IAgent _agent;

    private async void OnTimerElapsed(object sender, EventArgs e)
    {
        var reply = await _agent.ProcessAutoEventAsync("murmur", null);
        OnMurmur?.Invoke(this, reply);
    }
}
```

## 依赖关系

Agent 依赖：LlmClient, PromptFormatter, ShortTermMemory, MidTermMemory, LongTermMemory, ToolService, JsPluginLoader, AgentMoodTracker, 2dRenderer, DatabaseService

依赖 Agent：Form1（UI层）, AutoEventService

## 单元测试

### 测试要点

| 测试用例 | 预期结果 |
| ---------- | ---------- |
| 处理用户输入返回回复 | ProcessUserInputAsync 返回非空字符串 |
| 处理自动事件返回回复 | ProcessAutoEventAsync 返回非空字符串 |
| 记忆总结返回摘要 | SummarizeMemoryAsync 返回非空摘要 |
| 关键词提取返回三个词 | ExtractKeywordsAsync 返回三个非空关键词 |
| 重要度评估返回 0-100 | EvaluateImportanceAsync 返回 0-100 的整数 |
| 工具调用返回结果 | ProcessToolCallAsync 返回执行结果 |
| 插件调用返回结果 | ProcessPluginCallAsync 返回执行结果 |
| MCP调用返回结果 | ProcessMcpCallAsync 返回执行结果 |
| 获取状态返回有效信息 | GetStatus 返回非空状态 |

### 测试方法

```csharp
[Fact]
public async Task ProcessUserInput_ShouldReturnReply()
{
    var agent = new Agent(/* mock dependencies */);
    var reply = await agent.ProcessUserInputAsync("你好");
    Assert.False(string.IsNullOrEmpty(reply));
}

[Fact]
public async Task ProcessAutoEvent_ShouldReturnReply()
{
    var agent = new Agent(/* mock dependencies */);
    var reply = await agent.ProcessAutoEventAsync("murmur");
    Assert.False(string.IsNullOrEmpty(reply));
}

[Fact]
public async Task EvaluateImportance_ShouldReturnValidScore()
{
    var agent = new Agent(/* mock dependencies */);
    var score = await agent.EvaluateImportanceAsync("用户提到下周要去面试");
    Assert.InRange(score, 0, 100);
}

[Fact]
public void GetStatus_ShouldReturnValidStatus()
{
    var agent = new Agent(/* mock dependencies */);
    var status = agent.GetStatus();
    Assert.NotNull(status);
    Assert.False(string.IsNullOrEmpty(status.CurrentMood));
}

[Fact]
public async Task ProcessToolCall_NonExisting_ShouldReturnError()
{
    var agent = new Agent(/* mock dependencies */);
    var result = await agent.ProcessToolCallAsync("nonexistent", "");
    Assert.NotNull(result);
}
```

## 配置参数

| 参数 | 默认值 | 说明 |
| EnableStructuredResponse | true | 是否启用LLM结构化响应解析 |
| MaxActionsPerResponse | 5 | 单次LLM响应最大执行动作数 |
| EnableMidTermMemoryOnChat | true | 对话时是否允许LLM主动录入中期记忆 |
| EnableLongTermRecall | true | 对话时是否检索长期记忆注入上下文 |
| FunctionModel | (同主模型) | 函数模式使用的模型（可指定更便宜的模型） |
