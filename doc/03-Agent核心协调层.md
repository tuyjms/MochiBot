# Agent 核心协调层

## 模块概述

Agent 是系统的**大脑**和**中枢神经**，负责协调所有子模块。它订阅事件调度器的事件，处理用户输入和系统事件，协调 LLM 调用、工具执行、记忆管理、情绪管理等。

### 两种 LLM 调用模式

| 模式 | 场景 | 说明 |
|------|------|------|
| **对话模式** | 用户聊天、自动事件（碎碎念/关怀） | LLM 扮演"AI女友"角色，通过结构化响应控制工具/记忆/情绪 |
| **函数模式** | 记忆总结、关键词提取、重要度评估 | LLM 被当作纯函数调用，直接调用 LlmClient，不走人格层，保证稳定性 |

## 核心接口

```csharp
public interface IAgent
{
    // ========== 心情记录器（集成到 Agent 内部） ==========

    /// <summary>当前情绪</summary>
    AgentMood CurrentMood { get; }

    /// <summary>情绪变化时触发的事件（UI订阅以更新头像）</summary>
    event EventHandler<AgentMood>? MoodChanged;

    /// <summary>手动设置情绪（外部触发，如摸摸她）</summary>
    void SetMood(AgentMood mood);

    /// <summary>根据系统事件自动切换情绪</summary>
    void UpdateMoodByEvent(string eventType);

    /// <summary>获取当前情绪对应的表情图片路径</summary>
    string GetMoodImagePath();


    // ========== 事件处理（由事件调度器触发） ==========

    /// <summary>处理用户消息事件</summary>
    Task<string> ProcessUserInputAsync(string message);

    /// <summary>处理系统自动事件（碎碎念、用眼提醒、深夜关怀）</summary>
    Task<string> ProcessAutoEventAsync(string eventType, string? eventData = null);


    // ========== 函数模式（LLM作为纯函数） ==========

    /// <summary>总结短期记忆（溢出时调用）</summary>
    Task<string> SummarizeMemoryAsync(string chatHistory);

    /// <summary>从事件描述中提取关键词（主谓宾）</summary>
    Task<(string kw1, string kw2, string kw3)> ExtractKeywordsAsync(string description);

    /// <summary>评估一段对话的重要度（0-100）</summary>
    Task<int> EvaluateImportanceAsync(string content);


    // ========== 工具/插件/MCP调用 ==========

    /// <summary>执行工具调用</summary>
    Task<string> ProcessToolCallAsync(string toolName, string parameters);

    /// <summary>执行DLLMOD插件调用</summary>
    Task<string> ProcessPluginCallAsync(string pluginName, string parameters);

    /// <summary>执行MCP服务器工具调用</summary>
    Task<string> ProcessMcpCallAsync(string serverName, string toolName, string parameters);


    // ========== 状态查询 ==========

    /// <summary>获取当前Agent状态摘要</summary>
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

## 集成到Agent的模块

### 心情记录器（已内联到 Agent）

心情记录器已完全集成到 Agent 中，不再作为独立服务或外部依赖。Agent 直接管理情绪状态：

```csharp
// Agent 内部直接管理情绪
private AgentMood _currentMood = AgentMood.Neutral;
public event EventHandler<AgentMood>? MoodChanged;

// 设置情绪
public void SetMood(AgentMood mood)
{
    if (_currentMood == mood) return;
    _currentMood = mood;
    MoodChanged?.Invoke(this, mood);
    // 记录到数据库
    if (_databaseService != null)
        _ = _databaseService.LogMoodChangeAsync(mood, _lastEvent);
}

// 根据事件切换情绪
public void UpdateMoodByEvent(string eventType)
{
    var newMood = eventType switch
    {
        "LateNight" or "Sleepy" => AgentMood.Sleepy,
        "LongWork" => AgentMood.Neutral,
        "Idle" => AgentMood.Sad,
        "Active" => AgentMood.Neutral,
        "Pet" => AgentMood.Touched,
        "Compliment" => AgentMood.Happy,
        "Angry" => AgentMood.Angry,
        _ => _currentMood
    };
    SetMood(newMood);
}
```

情绪切换规则：

| 触发事件 | 切换至情绪 | 说明 |
|---------|-----------|------|
| 用户长时间未交互（>30min） | Sad | 感到委屈 |
| 深夜时段（23:00-06:00） | Sleepy | 困倦状态 |
| 用户消息含"摸摸""摸头""抱抱"等关键词 | Touched | 被摸头感动 |
| 用户消息含"夸""好看""可爱""喜欢你"等关键词 | Happy | 被夸奖开心 |
| 默认状态 | Neutral | 平静 |

情绪变化通过两种方式触发：
1. **Agent 自动检测**：`ProcessUserInputAsync` 中根据用户消息关键词和时间自动调用 `UpdateMoodByEvent()`
2. **LLM 主动切换**：LLM 在 actions 中返回 `mood_change` action，`ExecuteActionsAsync` 解析后调用 `SetMood()`

### 短期记忆 (ShortTermMemory)

短期记忆已集成到 Agent 中，采用环形缓冲区设计，固定容量，自动淘汰旧记录：

```csharp
// Agent 内部管理短期记忆
private readonly IShortTermMemory _shortTermMemory;

// 使用方式
_shortTermMemory.AddMessage("user", userMessage);
var recentMessages = _shortTermMemory.GetRecentMessages(10);
```

### 长期记忆 (LongMemory)

中期记忆和长期记忆已合并为 LongMemory 模块，统一管理重要信息的存储和检索：

```csharp
// Agent 内部管理长期记忆
private readonly ILongMemory _longMemory;

// 使用方式
await _longMemory.AddEntryAsync(entry);
var results = await _longMemory.SearchByKeywordsAsync(keywords);
```

## 对话模式：结构化响应协议

在对话模式下，LLM 的回复中除了自然语言文本外，可以附带结构化指令，Agent 解析后执行。

### Prompt 构建

Agent 使用 PromptFormatter 构建各类 prompt，工具描述按三层结构动态注入：

```csharp
// 第一层：基础工具（直接注入）
var baseTools = _toolManager.GetToolDefinitions();

// 第二层：心情附加工具（根据当前情绪动态注入）
var moodTools = _toolManager.GetMoodBasedTools(_currentMood);

// 第三层：DLLMOD/MCP工具（通过 list_plugins 间接获取）
// LLM 需要先调用 list_plugins 获取列表
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

## 核心处理流程

### 1. 处理用户输入（对话模式）

1. 事件调度器分发 UserMessage 事件
2. Agent 接收事件，短期记忆.AddMessage("user", message)
3. 构建完整 Prompt（含长期记忆检索）
4. LlmClient.SendChatAsync() 对话模式调用
5. 解析 LLM 响应：提取 reply 文本 + 解析 actions 数组
6. 遍历执行 actions：tool_call → ToolManager、mood_change → SetMood()、animation → Renderer
7. 短期记忆.AddMessage("assistant", reply)
8. 检查短期记忆是否溢出，溢出时调用函数模式评估重要度后录入 LongMemory
9. 根据用户消息关键词和时间自动检测情绪变化（DetectAndTriggerMoodEvent）
10. 返回 reply

### 2. 处理自动事件（对话模式）

1. AutoEventService 定时器触发，发布 SystemAuto 事件
2. Agent 接收事件，根据 EventType 构建对应 Prompt
3. LlmClient.SendChatAsync() 对话模式调用
4. 解析 LLM 响应，执行 actions
5. 短期记忆.AddMessage("assistant", reply)
6. 返回 reply

### 3. 长期记忆录入（函数模式）

1. 短期记忆溢出或重要度超过阈值时触发
2. 函数模式 EvaluateImportanceAsync() 评估重要度
3. 函数模式 ExtractKeywordsAsync() 提取关键词
4. 构建 LongMemoryEntry 录入

## 依赖关系

Agent 依赖：LlmClient, PromptFormatter, ShortTermMemory, ToolManager, ICharacterRenderer, IDatabaseService

**不依赖**：IAgentMoodTracker（已内联到 Agent 内部）

依赖 Agent：Form1（UI层）, AutoEventService

## 关于单元测试

Agent 核心协调层**不需要编写单元测试**。原因如下：

- Agent 不是独立的功能模块，而是 LLM 操作模块的"操作系统"
- Agent 的核心逻辑是协调和调度其他模块，其正确性依赖于各子模块的正确性
- Agent 的测试需要完整的 LLM 调用链，单元测试无法覆盖真实场景
- Agent 的正确性通过整体集成测试来验证

> 详见 `doc/00 - 写给viber的话.md` 中"不需要单元测试的模块"章节的说明。

## 配置参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| EnableStructuredResponse | true | 是否启用LLM结构化响应解析 |
| MaxActionsPerResponse | 5 | 单次LLM响应最大执行动作数 |
| EnableLongTermRecall | true | 对话时是否检索长期记忆注入上下文 |
| FunctionModel | (同主模型) | 函数模式使用的模型（可指定更便宜的模型） |
