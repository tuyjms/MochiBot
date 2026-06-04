# 视觉模型接入 Agent — 实现方案

## Context

MochiBot 当前的 Agent 只处理纯文本消息。`PersonalityConfig.VisionModels` 已预留但从未使用。需求是：让桌宠能"看到"用户屏幕，通过 VisionModel 将截图转为文字描述，注入到 ChatModel 的上下文中。

**VisionModel 的定位**：不是直接替代 ChatModel，而是作为"眼睛"——把图片翻译成文字描述，供没有视觉能力的 ChatModel 使用。

**两个触发场景**：
1. 深夜关怀事件触发时自动截图，让桌宠知道用户在干什么
2. ChatWindow 发送消息时自动附带截图（可在设置中关闭）

## 架构设计

```
触发点                    截图服务              视觉服务               Agent
──────────────────────────────────────────────────────────────────────────────
ChatWindow.SendMessage →                    → VisionService.DescribeAsync → 注入 userMessage
LateNight 事件通过    → ScreenshotService  → VisionService.DescribeAsync → 注入 userMessage
AutoEventFilter 触发     (WPF 截屏)            (VisionModel LLM)
```

核心思路：截图描述作为**文本前缀**注入 userMessage，不改变现有的消息格式和 LLM 调用链。

**设计约束：截图原始数据不进入聊天记录。** 截图 byte[] 仅用于 VisionService 调用 VisionModel，调用完毕后立即丢弃。注入 userMessage 的是纯文字描述（`【用户屏幕画面】{description}`），保存到 SQLite 聊天记录时只保留文字，不保留任何图片原始信息。

## 修改计划

### Step 1: 新增 `ScreenshotService` — 截图服务

**新建文件**: `Src/Services/ScreenshotService.cs`

- 使用 Win32 API (`user32.dll GetSystemMetrics`) 获取屏幕尺寸，`Graphics.CopyFromScreen` 截取全屏
- 输出 PNG `byte[]`
- 静态工具类，无状态，无需注册到 DI
- 依赖 `System.Drawing.Common` NuGet 包

```csharp
public static class ScreenshotService
{
    /// <summary>截取全屏，返回 PNG 字节数组</summary>
    public static byte[] CaptureScreen();
}
```

### Step 2: 新增 `VisionService` — 视觉转文字服务

**新建文件**: `Src/Services/VisionService.cs`

- 持有 VisionModel 的 `LlmClient` 实例（`SupportsVision = true`）
- 接收 PNG `byte[]`，构造 OpenAI 多模态消息（`ChatMessageContentPart.CreateImagePart`），发送给 VisionModel
- VisionModel prompt 为固定模板："请用中文简洁描述这张截图的内容，重点关注用户正在做什么"
- 返回文字描述（string）
- **合法性检查**：VisionModels 未配置 / 配置为空 / LlmClient 创建失败 → 整个视觉链路短路，`TryDescribeScreenAsync` 直接返回 null
- **运行时检查**：截图 byte[] 为空或异常 → 返回 null；VisionModel 调用失败（网络/超时/模型不支持图片）→ 捕获异常、记录日志、返回 null
- 所有失败路径静默降级，不影响正常 ChatModel 流程

```csharp
public class VisionService
{
    private readonly LlmClient? _visionLlmClient;
    private readonly IConfigReader _configReader;
    private readonly bool _isAvailable;  // 构造时一次性判断：VisionModels 已配置且 LlmClient 创建成功

    public VisionService(IConfigReader configReader);
    public async Task<string?> TryDescribeScreenAsync();
}
```

`VisionService` 构造时的合法性检查链路：
1. `personality.VisionModels` 为 null 或空列表 → `_isAvailable = false`，日志 Warn
2. 取 `VisionModels[0]` 解析 provider/model → 创建 `LlmClient(supportsVision: true)`
3. 创建失败（provider 不存在等）→ `_isAvailable = false`，日志 Error
4. `TryDescribeScreenAsync` 入口检查 `_isAvailable`，false 直接返回 null
5. 截图失败（权限/异常）→ 捕获、日志 Error、返回 null
6. `SendVisionAsync` 调用失败 → 捕获、日志 Warn、返回 null

**关键点**：`LlmClient` 需要新增视觉能力标识和多模态消息支持：

在 `LlmClient` 中新增：
```csharp
/// <summary>该模型是否支持视觉输入（图片）</summary>
public bool SupportsVision { get; }

/// <summary>发送包含图片的多模态消息（仅 SupportsVision=true 时可用）</summary>
public virtual async Task<string> SendVisionAsync(byte[] imageBytes, string textPrompt)
{
    if (!SupportsVision)
        throw new InvalidOperationException($"模型 {model} 不支持视觉输入");

    var parts = new List<ChatMessageContentPart>
    {
        ChatMessageContentPart.CreateTextPart(textPrompt),
        ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), "image/png")
    };
    var messages = new List<ChatMessage> { new UserChatMessage(parts) };
    return await CallLlmAsync(messages);
}
```

构造函数新增 `supportsVision` 参数：
```csharp
public LlmClient(string provider, string model, IConfigReader configReader, bool supportsVision = false)
{
    SupportsVision = supportsVision;
    // ... 原有逻辑
}
```

`SupportsVision` 的赋值来源：由 `VisionService` 创建 LlmClient 时传入 `true`，普通 ChatModel 创建时保持默认 `false`。

### Step 3: `ModuleSettings` 新增设置项

**修改文件**: `Src/Core/Config/Models/ModuleSettings.cs`

```csharp
// ========== 视觉功能 ==========
/// <summary>聊天发送消息时是否自动截图附带上下文</summary>
public bool Vision_AutoScreenshotOnChat { get; set; } = true;
```

### Step 4: `MainAgent` 集成视觉服务

**修改文件**: `Src/Agent/Agent.cs`

新增字段：
```csharp
private VisionService _visionService;
```

构造函数中初始化：
```csharp
_visionService = new VisionService(configReader);
```

在 `ProcessEventInternalAsync` 中，构造 `userMessage` **之后**、调用 `ProcessWithLlmAsync` **之前**，注入截图描述逻辑：

```csharp
// 根据事件分类构建用户消息
switch (eventData.Category)
{
    case EventCategory.UserInput:
        userMessage = eventData.Info;
        break;
    case EventCategory.SystemAuto:
        userMessage = PromptBuilder.BuildAutoEventPrompt(eventData);
        break;
    default:
        return;
}

// ===== 视觉注入：截图 → VisionModel → 文字描述 =====
var shouldScreenshot = ShouldCaptureScreenshot(eventData);
if (shouldScreenshot)
{
    var description = await _visionService.TryDescribeScreenAsync();
    if (!string.IsNullOrEmpty(description))
    {
        userMessage = $"【用户屏幕画面】{description}\n\n{userMessage}";
    }
}

await ProcessWithLlmAsync(eventData, userMessage);
```

截图判断逻辑：
```csharp
private bool ShouldCaptureScreenshot(EventData eventData)
{
    if (eventData.Category == EventCategory.UserInput)
    {
        // 聊天消息：读取设置
        return _configReader.GetModuleSettings().Vision_AutoScreenshotOnChat;
    }
    if (eventData.Category == EventCategory.SystemAuto)
    {
        // 深夜关怀事件：始终截图
        try
        {
            using var doc = JsonDocument.Parse(eventData.Info);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return typeProp.GetString() == BuiltinTasks.LateNight;
            }
        }
        catch { }
    }
    return false;
}
```

### Step 5: `SettingsWindow` 新增开关 UI

**修改文件**: `Src/UI/SettingsWindow.xaml` + `Src/UI/SettingsWindow.xaml.cs` (或 `ModuleSettingsTabController`)

在模块设置区域添加一个 CheckBox：
```xml
<CheckBox x:Name="autoScreenshotCheckBox" Content="聊天时自动截图" Margin="0,5"/>
```

在 `LoadCurrentSettings` 中加载：
```csharp
autoScreenshotCheckBox.IsChecked = moduleSettings.Vision_AutoScreenshotOnChat;
```

在 `SaveButton_Click` 中保存（通过 `ModuleSettingsTabController.TryCollect`）：
```csharp
newModuleSettings.Vision_AutoScreenshotOnChat = autoScreenshotCheckBox.IsChecked == true;
```

### Step 6: 测试

**新建文件**: `MochiBot.Tests/Services/ScreenshotServiceTests.cs`
- 测试 `CaptureScreen` 返回非空 byte[]
- 测试返回的 byte[] 是有效 PNG

**新建文件**: `MochiBot.Tests/Services/VisionServiceTests.cs`
- 测试 VisionModels 未配置时返回 null
- 测试 `TryDescribeScreenAsync` 调用 LlmClient（mock）

**新建文件**: `MochiBot.Tests/Services/AgentVisionTests.cs`
- 测试 `ShouldCaptureScreenshot` 的判断逻辑
- 测试深夜事件触发截图
- 测试聊天消息根据设置决定是否截图

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `Src/Services/ScreenshotService.cs` | 新建 | Win32 全屏截屏工具类 |
| `Src/Services/VisionService.cs` | 新建 | 截图→VisionModel→文字描述 |
| `Src/Services/LlmClient.cs` | 修改 | 新增 `SendVisionAsync` 多模态消息方法 |
| `Src/Core/Config/Models/ModuleSettings.cs` | 修改 | 新增 `Vision_AutoScreenshotOnChat` |
| `Src/Agent/Agent.cs` | 修改 | 集成 VisionService，注入截图描述 |
| `Src/UI/SettingsWindow.xaml` | 修改 | 新增自动截图 CheckBox |
| `Src/UI/SettingsWindow.xaml.cs` | 修改 | 加载/保存截图设置 |
| `MochiBot.Tests/Services/ScreenshotServiceTests.cs` | 新建 | 截图服务测试 |
| `MochiBot.Tests/Services/VisionServiceTests.cs` | 新建 | 视觉服务测试 |

## 验证方式

1. `dotnet build` 确保编译通过
2. `dotnet test` 确保所有测试通过
3. 手动验证：
   - 启动应用，在 ChatWindow 发送消息，检查日志中是否有 `[VisionService]` 的截图和描述日志
   - 在设置中关闭自动截图，再次发消息，确认不再截图
   - 等待深夜关怀事件触发，确认事件时截图生效
