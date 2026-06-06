using System.Windows;

namespace MochiBot.Src.EventModels
{
    /// <summary>
    /// 聊天消息项
    /// </summary>
    public class ChatMessageItem
    {
        /// <summary>数据库主键，内存新增的消息为 0</summary>
        public int Id { get; set; }

        public string Sender { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsUser { get; set; }
        public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;
    }
}
