# Agent心理状态记录器 (AgentMoodTracker)

## 模块概述

负责记录和管理AI女友的当前情绪状态，根据用户交互和系统事件自动切换情绪，并触发UI更新（如更换头像表情）。

## 接口定义

### 情绪枚举

```csharp
/// <summary>
/// AI女友情绪状态枚举
/// </summary>
public enum AgentMood
{
    Happy,      // 开心 - 被夸奖、被摸头后
    Sad,        // 委屈 - 长时间未被关注
    Sleepy,     // 困倦 - 深夜时段
    Touched,    // 感动 - 被摸头后的反应
    Neutral,    // 平静 - 默认状态
    Teasing,    // 调皮 - 毒舌性格下的互动
    Angry,      // 生气 - 被频繁打扰
    Surprised   // 惊讶 - 意外事件触发
}
```

### 核心接口

```csharp
/// <summary>
/// 心理状态记录器接口
/// </summary>
public interface IAgentMoodTracker
{
    /// <summary>获取当前情绪</summary>
    AgentMood CurrentMood { get; }

    /// <summary>手动设置情绪（外部触发，如摸摸她）</summary>
    /// <param name="mood">目标情绪</param>
    void SetMood(AgentMood mood);

    /// <summary>根据系统事件自动切换情绪</summary>
    /// <param name="eventType">事件类型：LateNight, LongWork, Idle, Active, Pet, Compliment</param>
    void UpdateMoodByEvent(string eventType);

    /// <summary>获取当前情绪对应的表情图片路径</summary>
    string GetMoodImagePath();

    /// <summary>情绪变化时触发的事件（UI订阅以更新头像）</summary>
    event EventHandler<AgentMood> MoodChanged;
}
```

## 情绪切换规则

| 触发事件 | 切换至情绪 | 说明 |
| 用户长时间未交互（>30min） | Sad | 感到委屈 |
| 深夜时段（23:00-06:00） | Sleepy | 困倦状态 |
| 用户长时间工作（>2h无休息） | Neutral → 触发用眼提醒 | 关怀模式 |
| 用户点击"摸摸她" | Touched | 被摸头感动 |
| 用户点击"随机夸奖" | Happy | 被夸奖开心 |
| 毒舌性格下互动 | Teasing | 调皮状态 |
| 用户频繁发送消息 | Angry | 被烦到 |
| 默认状态 | Neutral | 平静 |

## 事件流

1. 用户交互或系统事件触发 UpdateMoodByEvent(eventType)
2. 情绪判定逻辑执行，切换当前情绪
3. MoodChanged 事件触发，执行以下操作：
   - UI 更新头像表情
   - 记录情绪日志到 DatabaseService
   - 通知 PromptFormatter 更新上下文

## 单元测试

### 测试要点

| 测试用例 | 预期结果 |
| ---------- | ---------- |
| 初始状态为 Neutral | CurrentMood == Neutral |
| 调用 SetMood(Happy) | CurrentMood 切换为 Happy，触发 MoodChanged 事件 |
| 调用 UpdateMoodByEvent("LateNight") | CurrentMood 切换为 Sleepy |
| 调用 UpdateMoodByEvent("Pet") | CurrentMood 切换为 Touched |
| 调用 UpdateMoodByEvent("Compliment") | CurrentMood 切换为 Happy |
| 连续调用 SetMood 多次 | 每次切换都触发 MoodChanged 事件 |
| 获取情绪对应的图片路径 | GetMoodImagePath() 返回非空路径 |

### 测试方法

```csharp
[Fact]
public void SetMood_ShouldChangeCurrentMood()
{
    var tracker = new AgentMoodTracker();
    tracker.SetMood(AgentMood.Happy);
    Assert.Equal(AgentMood.Happy, tracker.CurrentMood);
}

[Fact]
public void UpdateMoodByEvent_LateNight_ShouldSetSleepy()
{
    var tracker = new AgentMoodTracker();
    tracker.UpdateMoodByEvent("LateNight");
    Assert.Equal(AgentMood.Sleepy, tracker.CurrentMood);
}

[Fact]
public void MoodChanged_ShouldFireOnSetMood()
{
    var tracker = new AgentMoodTracker();
    var fired = false;
    tracker.MoodChanged += (_, _) => fired = true;
    tracker.SetMood(AgentMood.Happy);
    Assert.True(fired);
}
```

## 依赖关系

- **依赖**: `DatabaseService`（记录情绪日志）
- **被依赖**: `Form1`（UI订阅MoodChanged事件）、`PromptFormatter`（获取当前情绪构建prompt）、`2dRenderer`（根据情绪切换模型动作）
