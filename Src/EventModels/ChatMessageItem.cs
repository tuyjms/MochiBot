using System.Windows;

namespace MochiBot.Src.EventModels
{
    /// <summary>
    /// 聊天消息项
    /// </summary>
    public class ChatMessageItem
    {
        public string Sender { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsUser { get; set; }
        public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;
    }
}
