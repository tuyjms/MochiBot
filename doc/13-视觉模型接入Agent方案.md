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

**隐私设计**：截图功能涉及用户隐私，程序首次启动时弹出声明对话框，告知用户截图用途（原始数据不保存、不上传，仅作为上下文注入对话），用户确认后不再弹出。

## 修改计划

### Step 1: 配置项扩展

**修改文件**: `Src/Core/Config/Models/ModuleSettings.cs`

新增视觉功能配置项：

```csharp
// ========== 视觉功能 ==========

/// <summary>聊天发送消息时是否自动截图附带上下文</summary>
public bool Vision_AutoScreenshotOnChat { get; set; } = true;

/// <summary>深夜关怀事件触发时是否自动截图</summary>
public bool Vision_ScreenshotOnLateNight { get; set; } = true;

/// <summary>用眼提醒事件触发时是否自动截图</summary>
public bool Vision_ScreenshotOnEyeRest { get; set; } = false;

/// <summary>是否已阅读截图声明（首次启动弹窗确认后置为 true）</summary>
public bool Vision_ScreenshotConsent { get; set; } = false;
```

- `Vision_ScreenshotConsent` 仅标记"已阅读声明"，由 `ScreenshotService` 在截图前统一读取，未允许时直接返回 null。
- 各事件截图开关由 `ScreenshotPolicy` 统一判断，用户可通过 UI 逐事件配置。

### Step 2: 首次启动截图声明弹窗

截图功能涉及用户隐私，需要在程序首次启动时弹出声明对话框，告知用户截图用途，用户确认后不再弹出。

#### 2a. 声明对话框

**新建文件**: `Src/UI/ScreenshotConsentDialog.xaml` + `Src/UI/ScreenshotConsentDialog.xaml.cs`

对话框设计：
- 无边框窗口，宽 420，圆角，居中显示
- 标题："截图功能声明"
- 正文说明：截图仅用于 VisionModel 转文字描述，原始数据不保存、不上传，仅作为上下文注入对话
- 两个按钮：
  - "我已了解，开启截图功能" → `ConsentGranted = true`，关闭对话框
  - "暂不开启" → `ConsentGranted = false`，关闭对话框

```csharp
public partial class ScreenshotConsentDialog : Window
{
    public bool ConsentGranted { get; private set; }

    public ScreenshotConsentDialog();

    private void Agree_Click(object sender, RoutedEventArgs e)
    {
        ConsentGranted = true;
        DialogResult = true;
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        ConsentGranted = false;
        DialogResult = false;
    }
}
```

#### 2b. 启动检查逻辑

**修改文件**: `App.xaml.cs`

在 `OnStartup` 中，`Program.Initialize()` 之后、`MainWindow` 创建之前，插入声明检查逻辑：

```csharp
// 检查截图声明
var moduleSettings = configReader.GetModuleSettings();
if (!moduleSettings.Vision_ScreenshotConsent)
{
    var consentDialog = new ScreenshotConsentDialog();
    consentDialog.ShowDialog();
    // 无论用户同意与否，将状态持久化，避免每次启动都弹窗
    moduleSettings.Vision_ScreenshotConsent = true;
    configReader.SaveModuleSettings(moduleSettings);
}
```

**关键设计决策**：
- 无论用户点击"我已了解"还是"暂不开启"，`Vision_ScreenshotConsent` 均置为 `true` 并持久化，确保只弹一次。
- `Vision_ScreenshotConsent` 由 `ScreenshotService.CaptureScreen` 统一读取，未允许时直接返回 null，所有截图场景（聊天、深夜关怀等事件）在此处统一拦截。用户后续可在设置中自行配置 VisionModels 启用视觉功能。

### Step 3: 新增截图服务与截图策略

#### 3a. `ScreenshotService` — 截图基础服务

**新建文件**: `Src/Services/ScreenshotService.cs`

- 使用 Win32 API (`user32.dll GetSystemMetrics`) 获取屏幕尺寸，`Graphics.CopyFromScreen` 截取全屏
- **入口检查 `Vision_ScreenshotConsent`，未允许时直接返回 null**，所有截图场景统一在此拦截
- 输出 PNG `byte[]`，截图失败或无权限时返回 null
- 静态工具类，依赖 `System.Drawing.Common` NuGet 包

```csharp
public static class ScreenshotService
{
    /// <summary>截取全屏，返回 PNG 字节数组；未声明截图权限时返回 null</summary>
    public static byte[]? CaptureScreen(IConfigReader configReader);
}
```

内部逻辑：
```csharp
public static byte[]? CaptureScreen(IConfigReader configReader)
{
    // 总闸：未阅读截图声明，不截图
    if (!configReader.GetModuleSettings().Vision_ScreenshotConsent)
        return null;

    // ... Win32 截屏逻辑
}
```

#### 3b. `ScreenshotPolicy` — 截图策略模块

**新建文件**: `Src/Services/ScreenshotPolicy.cs`

将事件触发截图的判断逻辑从 Agent 中抽出，独立为策略模块。用户可通过 UI 逐事件配置是否截图。

```csharp
public static class ScreenshotPolicy
{
    /// <summary>根据事件类型和用户配置，判断是否应截屏</summary>
    public static bool ShouldCapture(EventData eventData, ModuleSettings settings)
    {
        if (eventData.Category == EventCategory.UserInput)
            return settings.Vision_AutoScreenshotOnChat;

        if (eventData.Category == EventCategory.SystemAuto)
        {
            var taskType = ExtractTaskType(eventData.Info);
            return taskType switch
            {
                BuiltinTasks.LateNight => settings.Vision_ScreenshotOnLateNight,
                BuiltinTasks.EyeRest  => settings.Vision_ScreenshotOnEyeRest,
                _ => false
            };
        }

        return false;
    }

    /// <summary>从事件 Info JSON 中提取 type 字段，解析失败返回 null</summary>
    private static string? ExtractTaskType(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return null;
        try
        {
            using var doc = JsonDocument.Parse(info);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
                return typeProp.GetString();
        }
        catch { }
        return null;
    }
}
```

**扩展方式**：后续新增事件类型时，只需在 `ModuleSettings` 加一个 `Vision_ScreenshotOnXxx` 配置项，在 `ScreenshotPolicy.ShouldCapture` 的 switch 中加一个分支，再在 UI 加一个 CheckBox。

### Step 4: 新增 `VisionService` — 视觉转文字服务

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

### Step 5: `LlmClient` 新增视觉能力

**修改文件**: `Src/Services/LlmClient.cs`

新增 `SupportsVision` 属性和 `SendVisionAsync` 多模态消息方法：

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

### Step 6: `MainAgent` 集成视觉服务

**修改文件**: `Src/Agent/Agent.cs`

新增字段：
```csharp
private VisionService _visionService;
```

构造函数中初始化：
```csharp
_visionService = new VisionService(configReader);
```

`ProcessWithLlmAsync` 签名新增 `screenDescription` 参数：
```csharp
private async Task ProcessWithLlmAsync(EventData eventData, string userMessage, string? screenDescription = null)
```

在 `ProcessWithLlmAsync` 内部，短期记忆写入和聊天历史持久化**之后**、构建用户上下文**之前**，将截图描述注入到实际送给 LLM 的 userMessage 中：

```csharp
// 1. 记录原始用户消息到短期记忆（不含截图描述）
_memoryCoordinator.ShortTermMemory.AddMessage(ChatRoles.User, userMessage);

// 持久化原始用户消息到 SQLite
_ = _chatHistoryRepo.SaveSingleMessageAsync(new ChatMessage { ... });

// 2. 如果有截图描述，拼接到送给 LLM 的消息中（不影响记忆和历史）
var llmMessage = userMessage;
if (!string.IsNullOrEmpty(screenDescription))
{
    llmMessage = $"【用户屏幕画面】{screenDescription}\n\n{userMessage}";
}

// 3. 构建 Prompt（用 llmMessage 代替 userMessage）
var userContext = _promptBuilder.BuildUserContext(llmMessage, longTermStr, recentMessages, lastJsonError);
```

在 `ProcessEventInternalAsync` 中，构造 `userMessage` **之后**、调用 `ProcessWithLlmAsync` **之前**，获取截图描述（仅获取，不混入 userMessage）：

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
// 注意：screenDescription 不混入 userMessage，避免进入记忆系统和聊天历史
string? screenDescription = null;
var settings = _configReader.GetModuleSettings();
if (ScreenshotPolicy.ShouldCapture(eventData, settings))
{
    screenDescription = await _visionService.TryDescribeScreenAsync();
}

await ProcessWithLlmAsync(eventData, userMessage, screenDescription);
```

截图判断已抽离到 `ScreenshotPolicy.ShouldCapture`，Agent 层仅做调用，不包含任何截图策略逻辑。

### Step 7: `SettingsWindow` 新增截图设置 UI

**修改文件**: `Src/UI/SettingsWindow.xaml` + `Src/UI/SettingsWindow.xaml.cs` (或 `ModuleSettingsTabController`)

在模块设置区域（或基础设置 Tab）添加"视觉功能"分组，包含各场景截图开关：

```xml
<!-- 视觉功能 -->
<Label Content="视觉功能" FontSize="13" FontWeight="SemiBold" Margin="0,6,0,2"/>
<CheckBox x:Name="autoScreenshotOnChatCheck" Content="聊天时自动截图" FontSize="13" Margin="0,2"/>
<CheckBox x:Name="screenshotOnLateNightCheck" Content="深夜关怀事件时截图" FontSize="13" Margin="0,2"/>
<CheckBox x:Name="screenshotOnEyeRestCheck" Content="用眼提醒事件时截图" FontSize="13" Margin="0,2,0,10"/>
```

在 `LoadCurrentSettings` 中加载：
```csharp
autoScreenshotOnChatCheck.IsChecked = moduleSettings.Vision_AutoScreenshotOnChat;
screenshotOnLateNightCheck.IsChecked = moduleSettings.Vision_ScreenshotOnLateNight;
screenshotOnEyeRestCheck.IsChecked = moduleSettings.Vision_ScreenshotOnEyeRest;
```

在 `SaveButton_Click` 中保存（通过 `ModuleSettingsTabController.TryCollect`）：
```csharp
newModuleSettings.Vision_AutoScreenshotOnChat = autoScreenshotOnChatCheck.IsChecked == true;
newModuleSettings.Vision_ScreenshotOnLateNight = screenshotOnLateNightCheck.IsChecked == true;
newModuleSettings.Vision_ScreenshotOnEyeRest = screenshotOnEyeRestCheck.IsChecked == true;
```

### Step 8: 测试

**新建文件**: `MochiBot.Tests/Services/ScreenshotServiceTests.cs`
- 测试 `CaptureScreen` 返回非空 byte[]
- 测试返回的 byte[] 是有效 PNG

**新建文件**: `MochiBot.Tests/Services/ScreenshotPolicyTests.cs`
- 测试聊天消息根据 `Vision_AutoScreenshotOnChat` 决定是否截图
- 测试深夜关怀事件根据 `Vision_ScreenshotOnLateNight` 决定是否截图
- 测试用眼提醒事件根据 `Vision_ScreenshotOnEyeRest` 决定是否截图
- 测试未配置的事件类型返回 false

**新建文件**: `MochiBot.Tests/Services/VisionServiceTests.cs`
- 测试 VisionModels 未配置时返回 null
- 测试 `TryDescribeScreenAsync` 调用 LlmClient（mock）

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `Src/Core/Config/Models/ModuleSettings.cs` | 修改 | 新增视觉功能配置项（4 个 bool） |
| `Src/UI/ScreenshotConsentDialog.xaml` | 新建 | 截图声明弹窗 UI |
| `Src/UI/ScreenshotConsentDialog.xaml.cs` | 新建 | 截图声明弹窗逻辑 |
| `App.xaml.cs` | 修改 | 启动时检查声明状态，未阅则弹窗 |
| `Src/Services/ScreenshotService.cs` | 新建 | Win32 全屏截屏 + consent 总闸 |
| `Src/Services/ScreenshotPolicy.cs` | 新建 | 截图策略：事件类型 → 配置项 → 是否截图 |
| `Src/Services/VisionService.cs` | 新建 | 截图→VisionModel→文字描述 |
| `Src/Services/LlmClient.cs` | 修改 | 新增 `SupportsVision` + `SendVisionAsync` |
| `Src/Agent/Agent.cs` | 修改 | 集成 VisionService + ScreenshotPolicy |
| `Src/UI/SettingsWindow.xaml` | 修改 | 新增视觉功能 CheckBox 组（3 项） |
| `Src/UI/SettingsWindow.xaml.cs` | 修改 | 加载/保存截图设置 |
| `MochiBot.Tests/Services/ScreenshotServiceTests.cs` | 新建 | 截图服务测试 |
| `MochiBot.Tests/Services/ScreenshotPolicyTests.cs` | 新建 | 截图策略测试 |
| `MochiBot.Tests/Services/VisionServiceTests.cs` | 新建 | 视觉服务测试 |

## 验证方式

1. `dotnet build` 确保编译通过
2. `dotnet test` 确保所有测试通过
3. 手动验证：
   - **声明弹窗**：删除 `appsettings.json` 或手动将 `Vision_ScreenshotConsent` 设为 `false`，启动应用，确认弹出截图声明对话框
   - **权限拦截**：点击"暂不开启"后，确认深夜关怀事件也不触发截图（`ScreenshotService` 日志返回 null）
   - **只弹一次**：重启应用，确认不再弹出声明对话框
   - **截图功能**：在 ChatWindow 发送消息，检查日志中是否有 `[VisionService]` 的截图和描述日志
   - **设置开关**：在设置中关闭自动截图，再次发消息，确认不再截图
   - **深夜关怀**：等待深夜关怀事件触发，确认事件时截图生效
