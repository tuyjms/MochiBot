using MochiBot.Src.EventModels;
using MochiBot.Src.Services;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 超上下文处理策略
    /// </summary>
    public enum OverflowStrategy
    {
        Truncate,   // 直接截断：丢弃最旧的记忆，保留最近的
        Summarize   // LLM总结：调用LLM将旧记忆压缩为摘要，保留摘要+最近记忆
    }

    /// <summary>
    /// 短期记忆接口
    /// </summary>
    public interface IShortTermMemory
    {
        /// <summary>添加一条对话记录</summary>
        /// <param name="role">角色（user/assistant）</param>
        /// <param name="content">消息内容</param>
        void AddMessage(string role, string content);

        /// <summary>获取最近N条对话记录（用于构建LLM上下文）</summary>
        /// <param name="count">获取条数，默认10条</param>
        List<ChatMessage> GetRecentMessages(int count = 10);

        /// <summary>清空所有记忆</summary>
        void Clear();

        /// <summary>获取所有记忆（用于持久化保存）</summary>
        List<ChatMessage> GetAllMessages();

        /// <summary>获取当前记忆条数</summary>
        int Count { get; }

        /// <summary>最大容量（默认50条）</summary>
        int Capacity { get; set; }

        /// <summary>超上下文处理策略（默认截断）</summary>
        OverflowStrategy OverflowStrategy { get; set; }

        /// <summary>获取上下文摘要（当策略为Summarize时调用LLM生成）</summary>
        string? ContextSummary { get; }

        /// <summary>手动触发LLM总结（将旧记忆压缩为摘要）</summary>
        Task<string> SummarizeAsync();

        /// <summary>是否有待执行的总结（溢出时自动标记）</summary>
        bool IsSummarizePending { get; }

    }
}
