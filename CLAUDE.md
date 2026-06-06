# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test

```bash
dotnet restore                              # 恢复 NuGet 依赖
dotnet run                                  # 编译并运行
dotnet test                                 # 运行全部测试
dotnet test --filter "FullyQualifiedName~TestClassName"  # 运行指定测试类
```

PowerShell 环境下 `&&` 不可用，用 `;` 分隔命令。

## CodeGraph 优先原则

本项目已配置 CodeGraph MCP 服务器（`codegraph_*` 工具），它是一个基于 tree-sitter 解析的代码知识图谱，索引了所有符号、调用关系和文件结构。

- **一切与代码相关的操作必须时必须优先使用 CodeGraph**——无论是查找符号、理解调用关系、追踪数据流、查找功能实现还是分析影响范围，都先用 `codegraph_*` 工具，**禁止**先用 Grep/Read/Agent 做文件扫描再人工拼凑
- 不要用 grep 去验证 codegraph 的结果——它来自 AST 解析，比文本搜索更准确
- 不要对多个符号逐个调用 `codegraph_node`，用 `codegraph_explore` 一次获取
- 如果 `.codegraph/` 目录不存在，提示用户运行 `codegraph init -i` 构建索引

## Architecture

MochiBot 是一个 .NET 10.0 WPF 桌面桌宠应用，通过 LLM 驱动虚拟宠物进行情感交互。核心依赖：`Microsoft.Data.Sqlite` (SQLite) + `OpenAI` SDK v2.10.0。

**事件驱动架构** — 所有交互（用户输入、系统定时事件、工具结果、情绪变化）都通过 `EventDispatcher` (Src/Core/Events/EventDispatcher.cs) 分发。事件分类定义在 `Src/EventModels/EventTypes.cs`（EventCategory 枚举）。`EventDispatcher` 同时承担 Cron 定时任务调度（每秒 tick 检查）。

Agent 本身不需要单元测试。


**工具系统** — `ToolService` (Src/Services/Tools/ToolService.cs) 统一调度三层工具：基础工具（timer、reply、list_plugins）→ 心情动画工具（cry/dance/yawn/blush/stomp）→ DLLMOD 插件。LLM 通过 actions JSON 中的 `type` 字段区分：`tool_call` / `plugin_call` / `mcp_call` / `mood_change` / `midterm_memory` / `animation`。

**配置管理** — `ConfigReader` (Src/Core/Config/ConfigReader.cs) 是单例，统一管理 `Resources/appsettings.json` 和 `Resources/Personalities/*_person.json`。支持热重载：通过 `ConfigChanged` 事件通知 Agent 重建 LlmClient 和记忆模块。**所有配置必须走 ConfigReader，禁止硬编码；所有日志必须走 `ConfigReader.Instance.Logger`**。

**人格系统** — `PersonalityConfig` 包含主人格描述 + 子人格列表（每个子人格有权重，用于随机切换）。人格文件命名规则：`{名称}_person.json`。

**记忆系统** — `ShortTermMemory` 是环形缓冲区（默认 50 条），溢出策略支持 Truncate/Summarize。`LongMemory` 合并了中期和长期记忆，基于 SQLite 存储。

**渲染层** — `CharacterRenderer` (Src/Renderer/CharacterRenderer.cs) 是情绪→动画状态机，扫描 `Resources/Images/{情绪}/{动作}/` 目录结构，通过 `SpriteRenderer` 逐帧渲染。UI 订阅 `MoodChange` 事件驱动动画切换。

**UI 层** — WPF 三个窗口：`MainWindow`（桌宠本体，桌面右下角，支持拖拽、气泡消息）→ `ChatWindow`（聊天界面，关闭时隐藏保留历史）→ `SettingsWindow`（配置编辑）。

## Project Structure

```
MochiBot/
├── Src/
│   ├── Agent/           # Agent 核心协调层 + 记忆模块 + ActionExecutor + PromptFormatter
│   ├── Core/
│   │   ├── Config/      # ConfigReader 单例 + 配置模型
│   │   ├── Database/    # SQLite 数据库服务 + Repository
│   │   └── Events/      # EventDispatcher + CronTask 定义
│   ├── EventModels/     # EventData, EventCategory, AgentMood, ChatMessage
│   ├── Services/        # LlmClient, ToolService, DllModLoader
│   ├── Renderer/        # CharacterRenderer + SpriteRenderer + SpriteSheetLoader
│   └── UI/              # MainWindow, ChatWindow, SettingsWindow (WPF)
├── Resources/
│   ├── appsettings.json # 主配置（Providers, AppSettings, ModuleSettings, CronTasks）
│   ├── Personalities/   # 人格配置 JSON
│   ├── Images/          # 角色动画帧 ({情绪}/{动作}/*.png + *.json)
│   └── Data/            # SQLite 数据库文件（自动生成）
├── MochiBot.Tests/      # xUnit 测试项目
└── doc/                 # 模块设计文档
```

## Git Commit Convention

所有 commit 必须使用中文前缀：`影响程度词：描述`。程度词必须是以下之一：
- **重构改动** — 代码结构重组，功能不变
- **兼容性改动** — 影响接口/配置/依赖
- **优化改动** — 性能优化或代码精简
- **功能改动** — 新增或修改功能
- **修复改动** — Bug 修复
- **文档改动** — 仅文档变更

禁止模糊描述如"修复了一些问题"。

## Key Conventions

- **互不干涉原则**: 不得修改自己负责范围外的模块，不得私自添加依赖库
- **日志**: 全部通过 `ConfigReader.Instance.Logger` 输出（Debug/Info/Warn/Error），禁止 `Console.WriteLine`
- **配置**: 新增配置项需在模块 md 文档 + `ConfigReader` + `appsettings.json` 三处同步
- **测试**: 使用 xUnit + SQLite 独立文件（每个测试 `Guid.NewGuid()` 文件名），测试按类别分文件夹
- **LLM 模型名格式**: `"{提供商}/{模型名}"`，如 `"LocalLMStudio/deepseek-v4-flash"`
