# vibe 必读

你要知道这是协作项目，不得想改就改。

## Git 提交规范

所有 commit 信息必须遵循以下格式：

```
影响程度词：一句话描述
```

### 影响程度词说明

| 程度词 | 含义 | 示例 |
|--------|------|------|
| 重构改动 | 代码结构重组，功能不变 | `重构改动：重命名测试项目 Mochi.Tests 为 MochiBot.Tests` |
| 兼容性改动 | 影响接口/配置/依赖的变更 | `兼容性改动：重构项目为WDF` |
| 优化改动 | 性能优化或代码精简 | `优化改动：去掉了不需要的逻辑` |
| 功能改动 | 新增或修改功能 | `功能改动：添加天气查询模块` |
| 修复改动 | Bug 修复 | `修复改动：修复数据库连接泄漏` |
| 文档改动 | 仅文档变更 | `文档改动：更新配置读取器文档` |

> 禁止使用模糊描述，如"修复了一些问题"、"更新代码"等。

## 术语速查

项目内常用的简写和行话，别猜含义，直接看这里。

| 简写 | 全称 | 说明 |
|------|------|------|
| 按键栏 | 按钮工具栏 | MainWindow 顶部的五个按钮：工具、聊天、设置、穿透、关闭。鼠标穿透开启时会自动隐藏 |
| web2 | WebView2 | 微软提供的嵌入式浏览器控件，用于渲染 VRM 模型（three.js + VRM SDK）。是桌宠的两种显示模式之一 |
| gif模式 | GIF 显示模式 | 桌宠的两种显示模式之一，用 `Image` 控件播放帧动画。另一种是 VRM 模式（通过 web2 渲染 3D 模型） |
| 鼠标穿透 | 点击穿透 | 通过 Win32 `WS_EX_TRANSPARENT` 实现，鼠标点击直接穿过窗口到达桌面下方，不影响操作 |
| 人格 | 角色人格 | 存储在 `Resources/Personalities/*.json` 的角色设定，决定 LLM 的回复风格和行为模式 |

## 互不干涉原则

- 提前说明自己要施工的模块，不得修改负责范围外的模块。否者会导致无法提交和合并代码分支
- 即使是为了优化也不能擅自修改负责范围外的模块，更不能私自添加依赖库

## 模块结构

如果模块包含多个文件，在目录下创建模块文件夹存放相关文件。例如：

```txt
工作目录/
├── DatabaseService.cs          # 单文件模块
├── MoodModule/                 # 多文件模块示例
│   ├── MoodRecorder.cs
│   ├── MoodAnalyzer.cs
│   └── ...
└── ...
```

## 配置管理规范

所有配置项必须通过 **配置读取器（ConfigReader）** 统一管理，禁止在各模块中硬编码配置参数。

### 新增配置项流程

1. **发起协商**：在对应模块的 md 文档中描述新增配置项的名称、类型、默认值、用途
2. **注册到配置读取器**：在 `doc/08-配置读取器.md` 中更新数据模型和 `appsettings.json` 结构
3. **实现代码**：在 `ConfigReader.cs` 中添加对应的属性或方法
4. **更新文档**：同步更新 `doc/01-项目架构总览.md`

> 禁止在各模块中直接硬编码配置参数，所有软件配置必须走 ConfigReader 统一流程。

## 日志规范

所有模块必须通过 **配置读取器的 Logger** 打印日志，禁止直接使用 `Console.WriteLine` 或其他日志方式。

- Logger 由 ConfigReader 统一初始化和管理
- 各模块通过 `ConfigReader.Instance.Logger` 获取日志实例
- 日志级别：Debug / Info / Warn / Error
- 日志输出到控制台和文件（`Resources/Logs/` 目录）

## 单元测试

因为在项目起步期，无法直接运行项目来测试模块。所以我写了这个。

- 单元测试一般在对应模块的md文档，需要的话自己实现一个测试流程，保证模块能正常运作
- 如果md文档没有描述测试流程或要求，自己写一个测试也可以
- md文档的单元测试篇章只有描述要求，没有代码

### 不需要单元测试的模块

**Agent 核心协调层** 不需要编写单元测试。原因如下：

- Agent 不是独立的功能模块，而是 LLM 操作模块的"操作系统"
- Agent 的正确性通过整体集成测试来验证

### 测试项目结构

测试项目位于 `MochiBot.Tests/` 目录下，使用 xUnit 框架 + SQLite 文件数据库（每个测试用例独立文件，自动清理）。测试文件按服务类别分文件夹存放。

```txt
MochiBot.Tests/
├── MochiBot.Tests.csproj         # 测试项目配置
├── Services/                     # 服务层测试
│   ├── DatabaseServiceTests.cs   # 数据库业务层测试
│   └── ConfigReaderTests.cs      # 配置读取器测试
├── Events/                       # 事件系统测试
├── Renderer/                     # 渲染器测试
└── Models/                       # 模型测试
```

### 运行测试

```bash
# 运行所有测试
dotnet test MochiBot.Tests/MochiBot.Tests.csproj

# 运行指定测试类（按名称筛选）
dotnet test MochiBot.Tests/MochiBot.Tests.csproj --filter "FullyQualifiedName~DatabaseServiceTests"
```

### 编写测试的注意事项

1. **测试项目引用主项目**：`MochiBot.Tests.csproj` 已通过 `<ProjectReference>` 引用主项目，可直接使用主项目的类和接口
2. **主项目排除测试文件**：`MochiBot.csproj` 中已添加 `<Compile Remove="MochiBot.Tests\**\*.cs" />`，避免主项目编译时误编译测试文件
3. **SQLite 连接池**：测试连接字符串需添加 `Pooling=False`，确保每个测试用例的数据库文件可被正确清理
4. **文件清理**：`Dispose()` 中使用 `try/catch` 包裹文件删除，避免因文件锁定导致测试失败
5. **独立数据库文件**：每个测试用例使用 `Guid.NewGuid()` 生成唯一文件名，避免并行测试冲突
6. **按文件夹分类**：测试文件按对应模块类别放入 `Services/`、`Models/` 等子文件夹，保持结构清晰

## 开发环境搭建

### 前置条件

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

### 克隆与运行

```bash
# 1. 克隆仓库
git clone https://github.com/tuyjms/MochiBot.git

# 2. 进入项目目录
cd MochiBot

# 3. 一键恢复所有 NuGet 包（自动从 nuget.org 下载依赖）
dotnet restore

# 4. 编译运行
dotnet run
```

> NuGet 包依赖已在 `.csproj` 中声明，`dotnet restore` 会自动下载所有需要的包，无需手动安装或提交 `packages/` 目录到 git。
