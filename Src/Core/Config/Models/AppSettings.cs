using static MochiBot.Src.Core.Constants;

namespace MochiBot.Src.Core.Config.Models
{
    /// <summary>
    /// 应用级配置
    /// </summary>
    public class AppSettings
    {
        /// <summary>合法日志级别选项</summary>
        public static readonly string[] ValidLogLevels = { "Debug", "Info", "Warn", "Error" };

        /// <summary>合法的关闭行为选项</summary>
        public static readonly string[] ValidCloseBehaviors = { "Exit", "Hide" };

        /// <summary>MaxActionsPerResponse 的上限</summary>
        public const int MaxActionsUpperBound = 20;

        /// <summary>用户名称（LLM 对用户的称呼）</summary>
        public string UserName { get; set; } = UserDefaults.DefaultUserName;

        /// <summary>当前激活的人格名称（对应 Resources/Personalities/ 下的 {名称}_person.json）</summary>
        public string ActivePersonality { get; set; } = "default";

        /// <summary>是否启用LLM结构化响应解析</summary>
        public bool EnableStructuredResponse { get; set; } = true;

        /// <summary>单次LLM响应最大执行动作数</summary>
        public int MaxActionsPerResponse { get; set; } = 5;

        /// <summary>对话时是否允许LLM主动录入中期记忆</summary>
        public bool EnableMidTermMemoryOnChat { get; set; } = true;

        /// <summary>对话时是否检索长期记忆注入上下文</summary>
        public bool EnableLongTermRecall { get; set; } = true;

        /// <summary>日志级别</summary>
        public string LogLevel { get; set; } = "Info";

        /// <summary>是否启用日志文件输出</summary>
        public bool LogToFile { get; set; } = true;

        /// <summary>是否启用日志控制台输出</summary>
        public bool LogToConsole { get; set; } = true;

        /// <summary>关闭主窗口后的行为：Exit=退出程序，Hide=隐藏到后台</summary>
        public string CloseBehavior { get; set; } = "Exit";

        /// <summary>鼠标穿透模式下窗口透明度（0.1~1.0）</summary>
        public double PassthroughOpacity { get; set; } = 0.3;
    }
}
