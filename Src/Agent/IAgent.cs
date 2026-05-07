using MochiBot.Src.Models;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// Agent 状态摘要
    /// </summary>
    public class AgentStatus
    {
        public string CurrentMood { get; set; } = string.Empty;
        public int ShortTermMemoryCount { get; set; }
        public int MidTermMemoryCount { get; set; }
        public int LongTermMemoryCount { get; set; }
        public bool IsProcessing { get; set; }
        public string LastEvent { get; set; } = string.Empty;
    }

    /// <summary>
    /// Agent 核心协调层接口
    /// 作为 LLM 与平台交互的唯一入口
    /// 通过事件调度器接收事件，处理完成后发布回复事件
    /// </summary>
    public interface IAgent
    {
        // ========== 心情记录器（集成到 Agent 内部） ==========

        /// <summary>获取当前情绪</summary>
        AgentMood CurrentMood { get; }

        // ========== 统一事件处理 ==========

        /// <summary>处理事件（用户输入、系统自动事件等统一入口）</summary>
        /// <param name="eventData">事件数据</param>
        Task ProcessEventAsync(EventData eventData);

        // ========== 状态查询 ==========

        /// <summary>获取当前Agent状态摘要</summary>
        AgentStatus GetStatus();
    }
}
