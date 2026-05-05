namespace catgirlwindow.Models;

/// <summary>
/// 对话消息模型
/// </summary>
public class ChatMessage
{
    /// <summary>角色：user / assistant / system</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>消息内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
