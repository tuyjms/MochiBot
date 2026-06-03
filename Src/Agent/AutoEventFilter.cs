using System.Text.Json;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using static MochiBot.Src.Core.Constants;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 自动事件过滤结果
    /// </summary>
    public enum AutoEventResult
    {
        /// <summary>继续处理（传给 LLM）</summary>
        Continue,
        /// <summary>已由内置逻辑处理完毕，无需继续</summary>
        Handled,
        /// <summary>条件不满足，跳过本次事件</summary>
        Skip
    }

    /// <summary>
    /// 自动事件过滤器
    /// 负责内置任务的条件判断和特殊处理：
    /// - 碎碎念（Murmur）：按权重随机决定走 LLM 还是内置文本
    /// - 用眼提醒（EyeRest）：检查屏幕前时长是否达到阈值
    /// - 空闲检测（IdleCheck）：检查离开时长是否达到阈值
    /// </summary>
    public class AutoEventFilter
    {
        private readonly IToolService _toolService;
        private readonly Action<string, string> _onMemoryLog;
        private readonly Action<EventData> _onPublish;

        private DateTime _lastActivityTime = DateTime.Now;

        public AutoEventFilter(
            IToolService toolService,
            Action<string, string> onMemoryLog,
            Action<EventData> onPublish)
        {
            _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
            _onMemoryLog = onMemoryLog ?? throw new ArgumentNullException(nameof(onMemoryLog));
            _onPublish = onPublish ?? throw new ArgumentNullException(nameof(onPublish));
        }

        /// <summary>记录用户活动时间（用户输入时调用）</summary>
        public void RecordUserActivity()
        {
            _lastActivityTime = DateTime.Now;
        }

        /// <summary>
        /// 统一入口：记录活动 + 内置任务短路 + 条件检查
        /// </summary>
        public AutoEventResult Update(EventData eventData)
        {
            // 用户输入时记录活动时间
            if (eventData.Category == EventCategory.UserInput)
            {
                _lastActivityTime = DateTime.Now;
            }

            // 系统自动事件才需要过滤
            if (eventData.Category != EventCategory.SystemAuto)
                return AutoEventResult.Continue;

            // 碎碎念：按权重随机决定走 LLM 还是内置文本
            if (TryHandleMurmur(eventData))
                return AutoEventResult.Handled;

            // 用眼提醒/空闲检测：条件不满足则跳过
            if (!ShouldProcessEvent(eventData))
                return AutoEventResult.Skip;

            return AutoEventResult.Continue;
        }

        /// <summary>
        /// 尝试处理碎碎念事件
        /// 根据权重和随机决定使用内置文本还是 LLM 生成回复
        /// </summary>
        /// <returns>true 表示已处理（无需继续处理），false 表示不是碎碎念事件或决定走 LLM</returns>
        public bool TryHandleMurmur(EventData eventData)
        {
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                    return false;

                var type = typeProp.GetString();
                if (type != BuiltinTasks.Murmur)
                    return false;

                // 从 parameters 中读取权重
                int weight = 30;
                if (doc.RootElement.TryGetProperty("parameters", out var paramsProp))
                {
                    int.TryParse(paramsProp.GetString(), out weight);
                }

                // 根据权重随机决定：roll < weight 时使用 LLM，否则使用内置文本
                var roll = Random.Shared.Next(100);
                if (roll < weight)
                {
                    // 使用 LLM 生成回复（返回 false 让调用方继续处理）
                    return false;
                }

                // 使用内置碎碎念文本（通过 ToolService 统一工具接口）
                var result = _toolService.ExecuteToolAsync(Tools.Murmur, "{}").GetAwaiter().GetResult();
                if (result.Success)
                {
                    using var resultDoc = JsonDocument.Parse(result.Data);
                    var text = resultDoc.RootElement.TryGetProperty(Tools.Murmur, out var murmurProp)
                        ? murmurProp.GetString() ?? ""
                        : "";

                    if (!string.IsNullOrEmpty(text))
                    {
                        _onMemoryLog(ChatRoles.Assistant, text);
                        _onPublish(new EventData
                        {
                            Category = EventCategory.ToolResult,
                            Trigger = EventTrigger.System,
                            Info = JsonSerializer.Serialize(new
                            {
                                type = Tools.Reply,
                                content = text,
                                source = Tools.Murmur
                            })
                        });
                    }
                }

                return true; // 已处理，无需继续
            }
            catch
            {
                return false; // 解析失败，交给 LLM 处理
            }
        }

        /// <summary>
        /// 系统自动事件条件检查
        /// 检查用眼提醒和空闲检测的前置条件，不满足则跳过 LLM 调用
        /// </summary>
        /// <returns>true 表示应该处理，false 表示条件不满足应跳过</returns>
        public bool ShouldProcessEvent(EventData eventData)
        {
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                    return true;

                var type = typeProp.GetString();

                // 用眼提醒：检查是否达到阈值（默认 120 分钟）
                if (type == BuiltinTasks.EyeRest)
                {
                    int threshold = 120;
                    if (doc.RootElement.TryGetProperty("parameters", out var p) && int.TryParse(p.GetString(), out var t))
                        threshold = t;

                    var elapsed = (DateTime.Now - _lastActivityTime).TotalMinutes;
                    if (elapsed < threshold)
                        return false;
                }

                // 空闲检测：检查是否达到阈值（默认 5 分钟）
                if (type == BuiltinTasks.IdleCheck)
                {
                    int threshold = 5;
                    if (doc.RootElement.TryGetProperty("parameters", out var p) && int.TryParse(p.GetString(), out var t))
                        threshold = t;

                    var idleMinutes = (DateTime.Now - _lastActivityTime).TotalMinutes;
                    if (idleMinutes < threshold)
                        return false;
                }
            }
            catch { }

            return true;
        }
    }
}
