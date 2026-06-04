# Agent 核心协调层

## 模块概述

Agent 是系统的**大脑**和，负责协调所有子模块。它订阅事件调度器的事件，处理用户输入和系统事件。

## 集成到Agent的模块

### 心情记录器（已内联到 Agent）

心情记录器已完全集成到 Agent 中，不再作为独立服务或外部依赖。Agent 直接管理情绪状态：

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

### 长期记忆 (LongMemory)

中期记忆和长期记忆已合并为 LongMemory 模块，统一管理重要信息的存储和检索：

## 对话模式：结构化响应协议

在对话模式下，LLM 的回复中除了自然语言文本外，可以附带结构化指令，Agent 解析后执行。

## 核心处理流程

### 1. 处理用户输入

1. 事件调度器分发 UserInput 事件
2. Agent 接收事件，短期记忆.AddMessage("user", message)
3. **ChatHistoryRepository.SaveSingleMessageAsync(User)** — 实时持久化到 SQLite
4. 构建完整 Prompt（含长期记忆检索）
5. LlmClient.SendChatAsync() 对话模式调用
6. 解析 LLM 响应：提取 reply 文本 + 解析 actions 数组
7. 遍历执行 actions：tool_call → ToolService、mood_change → SetMood()、animation → Renderer
8. 短期记忆.AddMessage("assistant", reply)
9. **ChatHistoryRepository.SaveSingleMessageAsync(Assistant)** — 实时持久化到 SQLite
10. 检查短期记忆是否溢出，溢出时调用函数模式评估重要度后录入 LongMemory
11. 根据用户消息关键词和时间自动检测情绪变化（DetectAndTriggerMoodEvent）
12. 返回 reply

> **启动恢复**：应用重启时，`MemoryCoordinator.WarmUpFromDatabaseAsync` 从 SQLite 加载最近消息预热 ShortTermMemory，`ChatWindow.LoadHistoryAsync` 恢复 UI 消息列表。

### 2. 处理自动事件

1. EventDispatcher（定时任务） 定时器触发，发布 SystemAuto 事件
2. Agent 接收事件，根据 EventType 构建对应 Prompt
3. LlmClient.SendChatAsync() 对话模式调用
4. 解析 LLM 响应，执行 actions
5. 短期记忆.AddMessage("assistant", reply)
6. 返回 reply

### 3. 长期记忆录入

1. 短期记忆溢出或重要度超过阈值时触发
2. 函数模式 EvaluateImportanceAsync() 评估重要度
3. 函数模式 ExtractKeywordsAsync() 提取关键词
4. 构建 LongMemoryEntry 录入

## 依赖关系

Agent 依赖：LlmClient, PromptBuilder, ShortTermMemory, LongMemory, ToolService, ChatHistoryRepository, EventDispatcher, MoodLogRepository

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
