# Live2D渲染器 (Live2dRenderer)

## 模块概述

负责渲染 Live2D 模型，根据 AI 女友的当前情绪状态切换模型动作和表情，提供生动的视觉交互体验。

## 接口定义

### 核心接口

```csharp
/// <summary>
/// Live2D渲染器接口
/// </summary>
public interface ILive2dRenderer
{
    /// <summary>初始化Live2D模型</summary>
    /// <param name="modelPath">模型文件路径</param>
    Task InitializeAsync(string modelPath);

    /// <summary>根据情绪切换模型动作和表情</summary>
    /// <param name="mood">目标情绪</param>
    void SetMotion(AgentMood mood);

    /// <summary>播放指定动画（如拥抱、摸头等特殊交互）</summary>
    /// <param name="animationName">动画名称</param>
    void PlayAnimation(string animationName);

    /// <summary>设置模型透明度</summary>
    /// <param name="opacity">透明度值 0.0-1.0</param>
    void SetOpacity(double opacity);

    /// <summary>设置模型在窗口中的位置</summary>
    /// <param name="x">X坐标</param>
    /// <param name="y">Y坐标</param>
    void SetPosition(int x, int y);

    /// <summary>释放渲染资源</summary>
    void Dispose();
}
```

## 情绪-动作映射表

| 情绪 | 动作/表情 | 说明 |

| Happy | 微笑 + 眨眼 | 开心时的默认表情 |
| Sad | 低头 + 委屈眼神 | 委屈时的表情 |
| Sleepy | 打哈欠 + 眯眼 | 困倦时的表情 |
| Touched | 脸红 + 微笑 | 被摸头后的反应 |
| Neutral | 平静注视 | 默认状态 |
| Teasing | 坏笑 + 歪头 | 调皮时的表情 |
| Angry | 皱眉 + 扭头 | 生气时的表情 |
| Surprised | 瞪大眼 + 张嘴 | 惊讶时的表情 |

## 特殊动画

| 动画名称 | 触发场景 | 描述 |

| hug | 双击头像 / 拥抱功能 | 张开双臂的拥抱动作 |
| pet_head | 点击"摸摸她" | 头上出现抚摸的手 |
| wave | 用户打开工具菜单 | 挥手打招呼 |
| blink | 定时触发 | 眨眼（循环） |

## 事件流

AgentMoodTracker.MoodChanged 事件触发后，调用 SetMotion(mood) 切换模型动作，Live2D 渲染引擎播放对应动作并切换面部表情。

## 单元测试

### 测试要点

| 测试用例 | 预期结果 |
|----------|----------|
| 初始化模型后状态就绪 | InitializeAsync 完成后可正常调用其他方法 |
| 设置情绪切换动作 | SetMotion(Happy) 切换到对应动作 |
| 播放动画不抛异常 | PlayAnimation("hug") 正常执行 |
| 设置透明度生效 | SetOpacity(0.5) 后透明度改变 |
| 设置位置生效 | SetPosition(100, 200) 后位置改变 |
| 释放资源后状态清理 | Dispose 后资源释放 |

### 测试方法

```csharp
[Fact]
public async Task Initialize_ShouldCompleteSuccessfully()
{
    var renderer = new Live2dRenderer();
    await renderer.InitializeAsync("./Resources/Models/model.moc3");
    // 初始化成功，无异常抛出
}

[Fact]
public void SetMotion_ShouldNotThrow()
{
    var renderer = new Live2dRenderer();
    renderer.SetMotion(AgentMood.Happy);
    // 动作切换正常，无异常
}

[Fact]
public void PlayAnimation_ShouldNotThrow()
{
    var renderer = new Live2dRenderer();
    renderer.PlayAnimation("hug");
    // 动画播放正常，无异常
}

[Fact]
public void SetOpacity_ShouldChangeOpacity()
{
    var renderer = new Live2dRenderer();
    renderer.SetOpacity(0.5);
    // 透明度设置正常
}
```

## 依赖关系

- **依赖**: Live2D SDK（第三方渲染引擎）
- **被依赖**: `Form1`（UI层承载渲染控件）、`AgentMoodTracker`（情绪变化驱动动作切换）
