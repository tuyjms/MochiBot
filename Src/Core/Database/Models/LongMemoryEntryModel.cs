namespace MochiBot.Src.Core.Database.Models
{
    /// <summary>
    /// 长期记忆数据库模型
    /// </summary>
    public class LongMemoryEntryModel
    {
        public string Id { get; set; } = string.Empty;
        public string Keyword1 { get; set; } = string.Empty;
        public string Keyword2 { get; set; } = string.Empty;
        public string Keyword3 { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string EventTimestamp { get; set; } = string.Empty;
        public int Importance { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string LastAccessedAt { get; set; } = string.Empty;
        public int AccessCount { get; set; }
    }
}
