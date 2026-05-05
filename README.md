# MochiBot - AI女友桌面助手

一个基于 .NET 10.0 的 Windows 桌面应用 AI 桌宠。

## 功能特性

- 🎮 **角色渲染** — 动态角色渲染，根据情绪切换表情动作
- 💬 **AI 对话** — 接入 LLM，支持自然语言聊天
- 🧠 **三层记忆系统** — 短期记忆（环形缓冲区）+ 中期记忆 + 长期记忆（SQLite）
- 🎯 **工具功能** — 计时器、随机夸奖、摸摸她、天气预报
- 🔌 **JS 插件系统** — 支持动态加载 JavaScript 插件扩展功能
- 😊 **情绪系统** — 根据交互自动切换情绪（开心、委屈、困倦等）
- ⏰ **自动事件** — 碎碎念、用眼提醒、深夜关怀

## 快速开始

### 前置条件

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

### 克隆与运行

```bash
# 1. 克隆仓库
git clone https://github.com/tuyjms/MochiBot.git

# 2. 进入项目目录
cd MochiBot

# 3. 一键恢复 NuGet 依赖
dotnet restore

# 4. 编译运行
dotnet run
```

> NuGet 包依赖已在 `.csproj` 中声明，`dotnet restore` 会自动下载所有需要的包，无需手动安装。

## 项目结构

```txt
MochiBot/
├── Form1.cs                  # 主窗口（UI层）
├── Form1.Designer.cs         # 窗口设计器
├── LlmClient.cs              # LLM API 客户端
├── Program.cs                # 程序入口
├── appsettings.json          # 应用配置
├── Models/                   # 数据模型
│   ├── AgentMood.cs          # 情绪枚举
│   ├── ChatMessage.cs        # 聊天消息
│   ├── UserConfig.cs         # 用户配置
│   └── WeatherInfo.cs        # 天气信息
├── Services/                 # 服务接口层
│   ├── IAgent.cs             # Agent 核心协调
│   ├── IAgentMoodTracker.cs  # 情绪追踪
│   ├── IAutoEventService.cs  # 自动事件
│   ├── IDatabaseService.cs   # 数据库业务
│   ├── ILongTermMemory.cs    # 长期记忆
│   ├── IMidTermMemory.cs     # 中期记忆
│   ├── IShortTermMemory.cs   # 短期记忆
│   ├── IPromptFormatter.cs   # Prompt 格式化
│   └── IToolService.cs       # 工具功能
├── Plugins/                  # 插件接口
├── Renderer/                 # 渲染接口
├── Resources/                # 资源目录
│   ├── Data/                 # 运行时数据
│   ├── Images/               # 图片资源
│   └── Plugins/              # JS 插件
├── catgirlwindow.Tests/      # 单元测试项目
│   ├── Services/             # 服务层测试
│   │   └── DatabaseServiceTests.cs
│   └── Models/               # 模型测试（预留）
└── doc/                      # 项目文档
    ├── 00 - 写给viber的话.md  # 协作规范
    ├── 01-项目架构总览.md      # 架构总览
    ├── 02-Agent心理状态记录器.md
    ├── 03-Agent短期记忆.md
    ├── 04-数据库业务层.md
    ├── 05-JS插件加载器.md
    ├── 06-2D渲染器.md
    ├── 07-Prompt格式化器.md
    ├── 08-工具功能服务.md
    ├── 09-自动事件服务.md
    ├── 10-接口调用关系与协作图.md
    ├── 11-中期记忆.md
    ├── 12-长期记忆.md
    └── 13-Agent核心协调层.md
```

## 技术栈

- **框架**: .NET 10.0 (Windows Forms)
- **AI**: OpenAI API / 兼容接口
- **渲染**: GIF/PNG图集
- **存储**: SQLite (Microsoft.Data.Sqlite)
- **测试**: xUnit + coverlet
- **扩展**: JavaScript 插件引擎

## 运行测试

```bash
# 运行所有单元测试
dotnet test

# 运行带覆盖率报告的测试
dotnet test --collect:"XPlat Code Coverage"
```

## 贡献指南

请参阅 [doc/00 - 写给viber的话.md](doc/00%20-%20%E5%86%99%E7%BB%99viber%E7%9A%84%E8%AF%9D.md) 了解协作规范。
