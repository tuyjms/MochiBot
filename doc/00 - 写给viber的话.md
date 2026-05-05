# vibe 必读

你要知道这是协作项目，不得想改就改。

git commit 时必须描述清楚改动

## 互不干涉原则

- 提前说明自己要施工的模块，不得修改负责范围外的模块。否者会导致无法提交和合并代码分支
- 即使是为了优化也不能擅自修改负责范围外的模块，更不能私自添加依赖库

## 单元测试

因为在项目起步期，无法直接运行项目来测试模块。所以我写了这个。

- 单元测试一般在对应模块的md文档，需要的话自己实现一个测试流程，保证模块能正常运作
- 如果md文档没有描述测试流程或要求，自己写一个测试也可以

### 测试项目结构

测试项目位于 `catgirlwindow.Tests/` 目录下，使用 xUnit 框架 + SQLite 文件数据库（每个测试用例独立文件，自动清理）。

```txt
catgirlwindow.Tests/
├── catgirlwindow.Tests.csproj    # 测试项目配置
├── DatabaseServiceTests.cs       # 数据库业务层测试
└── UnitTest1.cs                  # 默认示例（可删除）
```

### 运行测试

```bash
# 运行所有测试
dotnet test catgirlwindow.Tests/catgirlwindow.Tests.csproj

# 运行指定测试类（按名称筛选）
dotnet test catgirlwindow.Tests/catgirlwindow.Tests.csproj --filter "FullyQualifiedName~DatabaseServiceTests"
```

### 编写测试的注意事项

1. **测试项目引用主项目**：`catgirlwindow.Tests.csproj` 已通过 `<ProjectReference>` 引用主项目，可直接使用主项目的类和接口
2. **主项目排除测试文件**：`catgirlwindow.csproj` 中已添加 `<Compile Remove="catgirlwindow.Tests\**\*.cs" />`，避免主项目编译时误编译测试文件
3. **SQLite 连接池**：测试连接字符串需添加 `Pooling=False`，确保每个测试用例的数据库文件可被正确清理
4. **文件清理**：`Dispose()` 中使用 `try/catch` 包裹文件删除，避免因文件锁定导致测试失败
5. **独立数据库文件**：每个测试用例使用 `Guid.NewGuid()` 生成唯一文件名，避免并行测试冲突

## 开发环境搭建

### 前置条件

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
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
