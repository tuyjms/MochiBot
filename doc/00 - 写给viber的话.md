# vibe 必读

你要知道这是协作项目，不得想改就改。

git commit 时必须描述清楚改动

## 互不干涉原则

- 提前说明自己要施工的模块，不得修改负责范围外的模块。否者会导致无法提交和合并代码分支
- 即使是为了优化也不能擅自修改负责范围外的模块，更不能私自添加依赖库

## 模块结构

如果模块包含多个文件，在 `Services/` 目录下创建模块文件夹存放相关文件。例如：

```txt
Services/
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
2. **注册到配置读取器**：在 `doc/14-配置读取器.md` 中更新数据模型和 `appsettings.json` 结构
3. **实现代码**：在 `ConfigReader.cs` 中添加对应的属性或方法
4. **更新文档**：同步更新 `doc/01-项目架构总览.md` 和 `doc/10-接口调用关系与协作图.md`

> 禁止在各模块中直接硬编码配置参数，所有配置必须走 ConfigReader 统一流程。

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

### 不需要单元测试的模块

**Agent 核心协调层** 不需要编写单元测试。原因如下：

- Agent 不是独立的功能模块，而是 LLM 操作模块的"操作系统"
- Agent 的核心逻辑是协调和调度其他模块，其正确性依赖于各子模块的正确性
- Agent 的测试需要完整的 LLM 调用链，单元测试无法覆盖真实场景
- Agent 的正确性通过整体集成测试来验证

### 测试项目结构

测试项目位于 `catgirlwindow.Tests/` 目录下，使用 xUnit 框架 + SQLite 文件数据库（每个测试用例独立文件，自动清理）。测试文件按服务类别分文件夹存放。

```txt
catgirlwindow.Tests/
├── catgirlwindow.SrcTests.csproj    # 测试项目配置
├── Services/                     # 服务层测试
│   ├── DatabaseServiceTests.cs   # 数据库业务层测试
│   └── ConfigReaderTests.cs      # 配置读取器测试
└── Models/                       # 模型测试（预留）
```

### 运行测试

```bash
# 运行所有测试
dotnet test catgirlwindow.SrcTests/catgirlwindow.Tests.csproj

# 运行指定测试类（按名称筛选）
dotnet test catgirlwindow.SrcTests/catgirlwindow.Tests.csproj --filter "FullyQualifiedName~DatabaseServiceTests"
```

### 编写测试的注意事项

1. **测试项目引用主项目**：`catgirlwindow.Tests.csproj` 已通过 `<ProjectReference>` 引用主项目，可直接使用主项目的类和接口
2. **主项目排除测试文件**：`catgirlwindow.csproj` 中已添加 `<Compile Remove="catgirlwindow.Tests\**\*.cs" />`，避免主项目编译时误编译测试文件
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
