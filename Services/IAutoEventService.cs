namespace catgirlwindow.Services;

/// <summary>
/// 自动事件服务接口
/// </summary>
public interface IAutoEventService
{
    /// <summary>启动所有定时任务</summary>
    void Start();

    /// <summary>停止所有定时任务</summary>
    void Stop();

    /// <summary>设置碎碎念间隔（分钟）</summary>
    /// <param name="minutes">间隔分钟数</param>
    void SetMurmurInterval(int minutes);

    /// <summary>启用/禁用碎碎念功能</summary>
    void EnableMurmur(bool enabled);

    /// <summary>获取碎碎念是否启用</summary>
    bool IsMurmurEnabled { get; }

    /// <summary>获取当前碎碎念间隔</summary>
    int MurmurInterval { get; }


    // ========== 事件 ==========

    /// <summary>碎碎念触发事件（参数为生成的唠叨文本）</summary>
    event EventHandler<string> OnMurmur;

    /// <summary>用眼提醒触发事件（参数为提醒文本）</summary>
    event EventHandler<string> OnEyeRestReminder;

    /// <summary>深夜关怀触发事件（参数为关怀文本）</summary>
    event EventHandler<string> OnLateNightCare;
}
