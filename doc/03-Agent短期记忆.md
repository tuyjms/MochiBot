# Agent短期记忆 (ShortTermMemory)

## 模块概述

负责存储和管理最近N条对话记录，用于构建LLM上下文，实现对话的连贯性和记忆能力。采用环形缓冲区（Circular Buffer）设计，固定容量，自动淘汰旧记录。

## 接口定义

### 数据模型

```csharp
/// <summary>
/// 对话消息模型
/// </summary>
public class ChatMessage
{
    /// <summary>角色：user / assistant / system</summary>
    public string Role { get; set; }

    /// <summary>消息内容</summary>
    public string Content { get; set; }

    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; }
}
```

### 超上下文处理策略枚举

```csharp
/// <summary>
/// 超上下文处理策略
/// </summary>
public enum OverflowStrategy
{
    Truncate,   // 直接截断：丢弃最旧的记忆，保留最近的
    Summarize   // LLM总结：调用LLM将旧记忆压缩为摘要，保留摘要+最近记忆
}
```

### 核心接口

```csharp
/// <summary>
/// 短期记忆接口
/// </summary>
public interface IShortTermMemory
{
    /// <summary>添加一条对话记录</summary>
    /// <param name="role">角色（user/assistant）</param>
    /// <param name="content">消息内容</param>
    void AddMessage(string role, string content);

    /// <summary>获取最近N条对话记录（用于构建LLM上下文）</summary>
    /// <param name="count">获取条数，默认10条</param>
    List<ChatMessage> GetRecentMessages(int count = 10);

    /// <summary>清空所有记忆</summary>
    void Clear();

    /// <summary>获取所有记忆（用于持久化保存）</summary>
    List<ChatMessage> GetAllMessages();

    /// <summary>获取当前记忆条数</summary>
    int Count { get; }

    /// <summary>最大容量（默认50条）</summary>
    int Capacity { get; set; }

    /// <summary>超上下文处理策略（默认截断）</summary>
    OverflowStrategy OverflowStrategy { get; set; }

    /// <summary>获取上下文摘要（当策略为Summarize时调用LLM生成）</summary>
    string? ContextSummary { get; }

    /// <summary>手动触发LLM总结（将旧记忆压缩为摘要）</summary>
    Task<string> SummarizeAsync();
}
```

## 实现要点

### 环形缓冲区设计

环形缓冲区固定容量为 N，初始化时 head=0, count=0。添加消息时依次填充，当 count 达到 N 时，新消息覆盖最旧的消息（head 循环移动）。

### 配置参数

| 参数 | 默认值 | 说明 |
| Capacity | 50 | 最大记忆条数 |
| TrimThreshold | 40 | 触发持久化的阈值（当count达到此值时自动保存到数据库） |
| OverflowStrategy | Truncate | 超上下文处理策略：Truncate（截断）/ Summarize（LLM总结） |
| SummaryPromptTemplate | （见下方） | LLM总结时使用的prompt模板 |
| SummaryReservedCount | 10 | Summarize策略下保留的最近消息条数（其余用于生成摘要） |

## 超上下文处理策略

当记忆数量达到 `Capacity` 上限时，根据 `OverflowStrategy` 配置采用不同的处理方式：

### 策略一：Truncate（直接截断）

当记忆已满时，丢弃最旧的 N 条记录（N = count - Capacity），保留最近的 Capacity 条直接作为 LLM 上下文。

**优点**：实现简单，零额外开销
**缺点**：丢失了早期对话信息，可能导致LLM"失忆"

### 策略二：Summarize（LLM总结）

当记忆已满时，将记忆拆分为旧记忆和新记忆两部分。旧记忆调用 LLM 总结生成摘要，新记忆保留最近 N 条。最终构建新的缓冲区：[摘要(system角色)] + [最近N条消息]。

**优点**：保留早期对话的核心信息，LLM记忆更完整
**缺点**：每次溢出需要一次额外的LLM调用，有性能和成本开销

### Summary Prompt 模板

```txt
请总结以下对话的核心内容，包括：
1. 用户的主要需求和偏好
2. 已经讨论过的重要话题
3. 尚未解决的问题或待办事项

要求：
- 用第三人称叙述
- 保留关键细节
- 控制在200字以内

对话内容：
{chat_history}
```

## 事件流

1. 用户发送消息或 AI 回复时调用 AddMessage() 添加记录
2. 检查容量是否溢出（count > Capacity）
   - 如果未溢出：正常添加，流程结束
   - 如果溢出：根据 OverflowStrategy 处理
     - Truncate 策略：丢弃最旧记录，保留最近 Capacity 条
     - Summarize 策略：拆分记忆 → 调用LLM总结旧记忆 → 用 [摘要 + 最近N条] 重建缓冲区 → 记录摘要到 ContextSummary
3. 当 count 达到 TrimThreshold 时，持久化到数据库

## 单元测试

### 测试要点

| 测试用例 | 预期结果 |
|----------|----------|
| 添加消息后 Count 增加 | AddMessage 后 Count == 1 |
| 获取最近 N 条消息 | GetRecentMessages(N) 返回正确条数 |
| 环形缓冲区溢出时自动覆盖 | 添加超过 Capacity 条后，最旧消息被覆盖 |
| Truncate 策略下溢出处理 | 溢出后 Count == Capacity，保留最近消息 |
| Clear 后清空所有记忆 | Clear 后 Count == 0 |
| GetAllMessages 返回全部 | 返回列表长度等于 Count |
| 设置 Capacity 后生效 | 修改 Capacity 后，溢出按新容量处理 |

### 测试方法

```csharp
[Fact]
public void AddMessage_ShouldIncreaseCount()
{
    var memory = new ShortTermMemory(capacity: 5);
    memory.AddMessage("user", "你好");
    Assert.Equal(1, memory.Count);
}

[Fact]
public void CircularBuffer_ShouldOverwriteOldest()
{
    var memory = new ShortTermMemory(capacity: 3);
    memory.AddMessage("user", "msg1");
    memory.AddMessage("user", "msg2");
    memory.AddMessage("user", "msg3");
    memory.AddMessage("user", "msg4"); // 溢出，覆盖 msg1
    var recent = memory.GetRecentMessages(3);
    Assert.Equal("msg2", recent[0].Content);
    Assert.Equal("msg4", recent[2].Content);
}

[Fact]
public void Clear_ShouldResetCount()
{
    var memory = new ShortTermMemory(capacity: 5);
    memory.AddMessage("user", "test");
    memory.Clear();
    Assert.Equal(0, memory.Count);
}

[Fact]
public void GetRecentMessages_ShouldReturnCorrectCount()
{
    var memory = new ShortTermMemory(capacity: 10);
    for (int i = 0; i < 5; i++)
        memory.AddMessage("user", $"msg{i}");
    var recent = memory.GetRecentMessages(3);
    Assert.Equal(3, recent.Count);
}
```

## 依赖关系

- **依赖**: `DatabaseService`（定期持久化记忆）
- **被依赖**: `LlmClient`（获取上下文构建prompt）、`PromptFormatter`（获取历史消息）
