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
