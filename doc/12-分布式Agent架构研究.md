# 12 - 分布式 Agent 架构研究

> **目标**：将 Agent "大脑"从桌面桌宠中分离，实现云端 Agent 平台 + 多端客户端（桌宠、QQ Bot、Web 等）的分布式架构。
>
> **研究方法**：基于 CodeGraph 对现有代码结构的 AST 级分析，识别可复用的接口边界和改造点。

---

## 1. 现状：单体架构边界分析

### 1.1 当前事件处理链

```
用户输入 / 系统定时事件 / UI 交互
    │
    ▼ Publish(EventData)
EventDispatcher (进程内 pub/sub 总线)
    │
    ▼ Subscribe(UserInput / SystemAuto / UiInteraction)
MainAgent (大脑)
    ├── PromptBuilder          — 构建 system + user prompt
    ├── LlmClient              — OpenAI SDK → 云端 LLM (已是网络调用)
    ├── ActionExecutor         — 解析并执行 actions[]
    │   ├── ToolService        — timer / reply / list_plugins
    │   ├── DllModLoader       — 本地 DLL 插件
    │   └── MCP 工具           — 外部 MCP 服务器
    ├── MoodManager            — 情绪状态机 + 事件发布
    ├── MemoryCoordinator      — 短期记忆(内存) + 长期记忆(SQLite)
    └── AutoEventFilter        — 内置任务条件过滤
    │
    ▼ MoodChange / Reply 事件
CharacterRenderer (动画状态机)
    │
    ▼
MainWindow / ChatWindow (WPF 渲染)
```

### 1.2 已有接口边界（可直接复用）

| 接口 | 位置 | 作用 | 分布式改造价值 |
|------|------|------|---------------|
| `IEventDispatcher` | `Src/Core/Events/IEventDispatcher.cs` | 事件总线，所有交互的统一入口 | ⭐⭐⭐ 可替换为远程消息总线 |
| `IToolService` | `Src/Services/IToolService.cs` | 工具调度，已支持插件/MCP | ⭐⭐ 工具可注册为远程服务 |
| `IAgent` | `Src/Agent/Agent.cs` | Agent 抽象接口 | ⭐⭐⭐ 可抽取为远程代理 |
| `LlmClient.CallLlmAsync` | `Src/Services/LlmClient.cs` | `virtual` 方法 | ⭐ 可重写为远程调用 |
| `EventProcessingQueue` | `Src/Agent/EventProcessingQueue.cs` | 串行事件队列 + 状态机 | ⭐⭐ 天然适配消息队列消费者模式 |
| `EventData` | `Src/EventModels/EventTypes.cs` | 统一事件数据结构 | ⭐⭐⭐ 可直接序列化为网络消息 |

### 1.3 关键耦合点

| 耦合组件 | 依赖 | 分离难度 |
|----------|------|----------|
| `MainAgent` → `LlmClient` | 构造函数注入，已是网络调用 | 🟢 低 — LLM 本身就在云端 |
| `MainAgent` → `MemoryCoordinator` | 直接引用，SQLite 本地文件 | 🟡 中 — 需迁移到云端 DB |
| `MainAgent` → `MoodManager` | 直接引用，内存状态 | 🟡 中 — 需同步给客户端 |
| `MainAgent` → `ToolService` | 本地执行 timer/DLLMOD | 🔴 高 — 本地工具需要回调机制 |
| `MainAgent` → `PromptBuilder` | 纯逻辑，无外部依赖 | 🟢 低 — 可直接搬到云端 |
| `CharacterRenderer` → `MoodChange` 事件 | 依赖情绪变化驱动动画 | 🟡 中 — 需要远程情绪同步 |

---

## 2. 分布式方案对比

### 2.1 方案 A：Agent Gateway（云端 Agent 网关）

```
┌─────────────┐     ┌─────────────────────┐     ┌──────────────┐
│  桌宠客户端   │────▶│                     │────▶│              │
│  (WPF 瘦客户端)│     │  Agent Gateway      │     │  云端 LLM    │
│  WebSocket   │◀────│  (ASP.NET Core)     │◀────│  (OpenAI等)  │
└─────────────┘     │                     │     └──────────────┘
                    │  ┌───────────────┐  │
┌─────────────┐     │  │  MainAgent    │  │     ┌──────────────┐
│  QQ Bot     │────▶│  │  Memory       │  │────▶│  云端数据库    │
│  (NapCat)   │     │  │  MoodManager  │  │     │  (Postgres)  │
│  WebSocket  │◀────│  │  ToolService  │  │     └──────────────┘
└─────────────┘     │  └───────────────┘  │
                    │                     │
┌─────────────┐     │  gRPC / WebSocket   │
│  Web 前端   │────▶│  REST API           │
│  HTTP/SSE   │◀────│                     │
└─────────────┘     └─────────────────────┘
```

**核心思路**：把 `MainAgent` 整体搬到云端 ASP.NET 服务，桌面端变成纯渲染 + 输入的瘦客户端。

| 维度 | 评估 |
|------|------|
| **优点** | 天然多端共享同一个 Agent 实例和记忆；Agent 24h 在线（不依赖桌面端开机）；工具执行在服务端统一管理 |
| **缺点** | 桌宠动画渲染仍需本地（帧动画传输不现实），需拆分 ActionExecutor 的本地/远程部分；本地工具（timer 桌面提醒、DLLMOD 插件）需要反向回调机制；需处理网络断线降级；部署成本高 |
| **适用场景** | 需要 24h 在线 + 跨机器部署的生产环境 |
| **改造量** | ⭐⭐⭐⭐ 大 |

### 2.2 方案 B：事件总线桥接（Message Bus）

```
┌─────────────┐                              ┌─────────────┐
│  桌宠客户端   │                              │  QQ Bot     │
│  (WPF)      │                              │  (NapCat)   │
│  ┌────────┐ │     ┌─────────────────┐      │  ┌────────┐ │
│  │Event   │ │────▶│                 │◀─────│──│Event   │ │
│  │Bridge  │ │     │  NATS / RabbitMQ│      │  │Bridge  │ │
│  └────────┘ │     │  消息总线        │      │  └────────┘ │
│  ┌────────┐ │     └────────┬────────┘      │             │
│  │Renderer│ │              │               │             │
│  └────────┘ │     ┌────────▼────────┐      │             │
└─────────────┘     │  Agent 服务      │      └─────────────┘
                    │  (独立进程/容器)  │
                    │  ┌─────────────┐ │     ┌──────────────┐
                    │  │ MainAgent   │ │────▶│  数据库       │
                    │  │ Memory      │ │     └──────────────┘
                    │  │ MoodManager │ │
                    │  └─────────────┘ │
                    └──────────────────┘
```

**核心思路**：用消息队列替代进程内 `EventDispatcher`，Agent 作为独立消费者。

| 维度 | 评估 |
|------|------|
| **优点** | 完全解耦，各端独立部署/重启；消息队列自带持久化、重试、负载均衡；桌宠端崩溃不影响 QQ Bot 继续服务 |
| **缺点** | 引入中间件依赖（NATS/RabbitMQ），运维复杂度上升；本地工具（timer、DLLMOD）需要注册为消息队列消费者；情绪/动画状态需要双向同步；延迟比进程内调用高 10-50ms |
| **适用场景** | 大规模多实例部署、需要高可用的企业级场景 |
| **改造量** | ⭐⭐⭐⭐⭐ 很大 |

### 2.3 方案 C：Agent-as-Process（本地 Agent + RPC 暴露）

```
┌─ 本机 ────────────────────────────────────────────┐
│                                                     │
│  ┌─ MochiBot Agent 进程 ─────────────────────┐    │
│  │  MainAgent + Memory + Tools + Mood         │    │
│  │  ┌──────────────┐                          │    │
│  │  │ gRPC Server  │  ProcessEvent(req)       │    │
│  │  │ / ProcessEvent│  → returns actions[]    │    │
│  │  │ / GetState    │  GetMood() → mood       │    │
│  │  └──────┬───────┘                          │    │
│  └─────────┼──────────────────────────────────┘    │
│            │                                        │
│  ┌─────────▼───────┐   ┌────────────────────┐     │
│  │  桌宠 WPF 客户端  │   │  QQ Bot 进程        │     │
│  │  gRPC Client     │   │  gRPC Client        │     │
│  │  + 本地渲染器     │   │  + NapCat SDK       │     │
│  └──────────────────┘   └────────────────────┘     │
└─────────────────────────────────────────────────────┘
```

**核心思路**：Agent 作为本机守护进程，通过 gRPC / Named Pipe 暴露给多个客户端。

| 维度 | 评估 |
|------|------|
| **优点** | 无需云端服务器，本机部署；gRPC 高性能（<1ms 延迟）；记忆/情绪状态集中管理；最小化改造 — 只需把 MainAgent 包一层 gRPC 服务 |
| **缺点** | Agent 受限于单机（不能跨机器）；桌宠端需要运行 Agent 进程才能工作；多客户端同时写入需排队（已有 EventProcessingQueue 串行处理） |
| **适用场景** | 本机多客户端（桌宠 + QQ Bot 共享），开发/测试阶段 |
| **改造量** | ⭐⭐⭐ 中等 |

### 2.4 方案 D：Hybrid — 云端思考 + 本地执行（推荐）

```
┌─ 云端 ────────────────────────────────────┐
│  Agent Brain Service (ASP.NET Core)       │
│  ┌─────────────────────────────────────┐  │
│  │  ProcessWithLlmAsync                │  │
│  │  ├── PromptBuilder                  │  │
│  │  ├── LlmClient         ──────────────┼──► 云端 LLM
│  │  ├── MemoryCoordinator ──────────────┼──► 云端 DB
│  │  └── AutoEventFilter                │  │
│  │                                      │  │
│  │  输出: actions[] JSON                │  │
│  └──────────────┬───────────────────────┘  │
│                 │                           │
│          gRPC / WebSocket API               │
└─────────────────┼───────────────────────────┘
                  │
    ┌─────────────┼──────────────┐
    │             │              │
┌───▼──────┐ ┌───▼──────┐ ┌─────▼─────────┐
│ 桌宠客户端 │ │ QQ Bot   │ │ Web Chat 前端  │
│ (WPF)    │ │ (NapCat) │ │ (React/Vue)   │
│          │ │          │ │               │
│ 本地执行:  │ │ 本地执行:  │ │ 本地执行:       │
│ 渲染动画   │ │ 发消息    │ │ 渲染气泡       │
│ timer    │ │          │ │               │
│ DLLMOD   │ │          │ │               │
│ 情绪动画   │ │          │ │               │
└──────────┘ └──────────┘ └───────────────┘
```

**核心思路**：把 Agent 拆成"思考层"（云端）和"执行层"（本地），思考层共享，执行层各端独立。

| 维度 | 评估 |
|------|------|
| **优点** | LLM 调用集中在云端，统一管理 API Key 和计费；本地工具在客户端执行，不需要网络回调；桌宠渲染保持 60fps 本地性能；多端共享记忆和人格（云端单一真相源）；QQ Bot 可以独立于桌宠运行 |
| **缺点** | 需要云端服务器；网络断线时需要降级策略；架构复杂度比单体高 |
| **适用场景** | 生产环境，需要 24h 在线 + 多端共享 + 本地渲染性能 |
| **改造量** | ⭐⭐⭐ 中等 |

---

## 3. 方案选择决策树

```
需要 24h 在线（QQ Bot 不依赖桌面端开机）？
├── 是 → 需要跨机器部署？
│   ├── 是 → 方案 A (Gateway) 或 方案 B (Message Bus)
│   │   └── 团队有运维能力？
│   │       ├── 是 → 方案 B (更健壮，自带重试/持久化)
│   │       └── 否 → 方案 A (更简单，单体部署)
│   └── 否 → 方案 D (Hybrid) ← 最佳平衡
│
└── 否 → 只需本机多客户端？
    ├── 是 → 方案 C (Agent-as-Process) ← 最小改造
    └── 否 → 维持现状（单体架构足够）
```

---

## 4. 推荐实施路径

### Phase 1：本机多客户端（方案 C）

**目标**：桌宠 + QQ Bot 共享同一个 Agent 实例，本机运行。

**改造点**：

| # | 任务 | 涉及文件 | 说明 |
|---|------|----------|------|
| 1 | 抽取 `IAgentBrain` 接口 | 新建 `Src/Agent/IAgentBrain.cs` | 定义 `ProcessEventAsync`, `GetState`, `GetMood` |
| 2 | 实现 gRPC 服务 | 新建 `Src/Agent/GrpcAgentService.cs` | 包装 MainAgent 为 gRPC 服务 |
| 3 | 桌宠端替换为 gRPC 客户端 | `Src/UI/MainWindow.xaml.cs` | 用 `AgentBrainClient` 代理替代直接引用 MainAgent |
| 4 | QQ Bot 对接 | NapCat 侧开发 gRPC 客户端 | 将 QQ 消息转为 EventData 发送 |
| 5 | 本地工具代理 | `Src/Agent/LocalToolProxy.cs` | timer/DLLMOD 等本地工具的反向调用机制 |

**协议定义**（gRPC proto）：

```protobuf
syntax = "proto3";
package mochibot;

service AgentBrain {
  // 核心：发送事件，返回 actions
  rpc ProcessEvent(ProcessEventRequest) returns (ProcessEventResponse);

  // 状态查询
  rpc GetAgentState(Empty) returns (AgentStateResponse);
  rpc GetMood(Empty) returns (MoodResponse);

  // 双向流（实时事件推送）
  rpc StreamEvents(stream ClientEvent) returns (stream AgentAction);
}

message ProcessEventRequest {
  string source = 1;       // "desktop" | "qq" | "web"
  EventData event = 2;
  repeated ChatMessage recent_messages = 3;  // 客户端本地短期记忆快照
}

message ProcessEventResponse {
  repeated AgentAction actions = 1;
  string reply_text = 2;
  AgentMood new_mood = 3;
  bool should_animate = 4;  // 是否需要播放动画
}

message EventData {
  int32 category = 1;
  int32 trigger = 2;
  string info = 3;
  int64 timestamp = 4;
}

message AgentAction {
  string type = 1;
  string name = 2;
  string content = 3;
  string parameters = 4;
  string mood = 5;
}
```

### Phase 2：云端部署（方案 D）

**目标**：Agent 思考层上云，24h 在线。

| # | 任务 | 说明 |
|---|------|------|
| 1 | 迁移 Memory 到云端 DB | SQLite → PostgreSQL/MySQL |
| 2 | 部署 Agent Brain Service | ASP.NET Core + gRPC |
| 3 | 客户端断线降级 | 本地缓存 + 简化回复 |
| 4 | API Key 集中管理 | 云端统一管理 LLM Provider 配置 |
| 5 | 多用户隔离 | 每个桌宠实例独立的记忆空间 |

### Phase 3：消息总线扩展（方案 B）

**目标**：多实例扩展、高可用。

| # | 任务 | 说明 |
|---|------|------|
| 1 | 引入 NATS/RabbitMQ | 替换进程内 EventDispatcher |
| 2 | Agent 水平扩展 | 多个 Agent 实例消费同一队列 |
| 3 | 状态外置 | 情绪/记忆状态存 Redis/DB |
| 4 | 消息持久化 | 确保事件不丢失 |

---

## 5. 关键技术决策

### 5.1 通信协议选择

| 协议 | 延迟 | 双向 | 复杂度 | 推荐场景 |
|------|------|------|--------|----------|
| **gRPC** | <1ms (本机) / ~10ms (网络) | 流式双向 | 中 | Phase 1 首选 |
| **WebSocket** | ~5ms | 天然双向 | 低 | Phase 2 实时推送 |
| **HTTP REST** | ~20ms | 单向 | 低 | 简单查询接口 |
| **Named Pipe** | <0.5ms | 双向 | 低 | 仅限本机，Phase 1 备选 |

### 5.2 序列化格式

- **gRPC 内部**：Protobuf（高效、强类型）
- **对外 API**：JSON（兼容性好）
- **消息队列**：JSON 或 MessagePack

### 5.3 断线降级策略

```
网络状态        桌宠行为                    QQ Bot 行为
─────────────────────────────────────────────────────
在线           正常（云端 Agent）           正常（云端 Agent）
断线           本地缓存回复 + 简化情绪      消息入队，恢复后批量处理
长时间断线      本地预设回复模式             告知用户 Agent 离线
```

### 5.4 本地工具远程化

本地工具（timer、DLLMOD）需要特殊处理：

```
云端 Agent 决策: {"type": "tool_call", "name": "timer", "parameters": {"seconds": 300}}
    │
    ▼ gRPC 返回给桌宠客户端
桌宠客户端执行: ToolService.ExecuteToolAsync("timer", ...)
    │
    ▼ 执行结果回传云端
云端 Agent 继续: 将工具结果加入短期记忆，决定下一步
```

---

## 6. 各方案改造量对比

| 方案 | 新增文件 | 修改文件 | 新增依赖 | 预估工时 |
|------|----------|----------|----------|----------|
| **A - Gateway** | ~8 | ~12 | ASP.NET Core, SignalR | 3-4 周 |
| **B - Message Bus** | ~12 | ~15 | NATS/RabbitMQ SDK | 4-5 周 |
| **C - Agent-as-Process** | ~4 | ~6 | gRPC SDK | 1-2 周 |
| **D - Hybrid** | ~6 | ~10 | gRPC + ASP.NET Core | 2-3 周 |

---

## 7. 总结

| 如果你需要... | 选择 |
|--------------|------|
| 最快实现本机桌宠 + QQ Bot 共享 | **方案 C**（1-2 周） |
| 24h 在线 + 多端共享 | **方案 D**（2-3 周） |
| 企业级高可用 + 水平扩展 | **方案 B**（4-5 周） |
| 最简单的一体化部署 | **方案 A**（3-4 周） |

**推荐路径**：C → D → B，渐进式演进，每一步都可独立交付价值。
