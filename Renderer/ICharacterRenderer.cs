using catgirlwindow.Models;

namespace catgirlwindow.Renderer;

/// <summary>
/// 角色动画渲染器接口
/// 支持 GIF 动画和 PNG 图集两种方式
/// </summary>
public interface ICharacterRenderer
{
    /// <summary>初始化渲染器</summary>
    /// <param name="resourcePath">角色资源文件夹路径</param>
    Task InitializeAsync(string resourcePath);

    /// <summary>根据情绪切换动画</summary>
    /// <param name="mood">目标情绪</param>
    void SetMotion(AgentMood mood);

    /// <summary>播放指定动画（如拥抱、摸头等特殊交互）</summary>
    /// <param name="animationName">动画名称</param>
    void PlayAnimation(string animationName);

    /// <summary>设置角色透明度</summary>
    /// <param name="opacity">透明度值 0.0-1.0</param>
    void SetOpacity(double opacity);

    /// <summary>设置角色在窗口中的位置</summary>
    /// <param name="x">X坐标</param>
    /// <param name="y">Y坐标</param>
    void SetPosition(int x, int y);

    /// <summary>释放渲染资源</summary>
    void Dispose();
}
