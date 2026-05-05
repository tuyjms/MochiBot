using catgirlwindow.Models;

namespace catgirlwindow.Renderer;

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
