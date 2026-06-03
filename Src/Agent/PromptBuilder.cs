using System.Text.Json;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using static MochiBot.Src.Core.Constants;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 提示词构建器
    /// 负责构建 System Prompt 和 User Context，以及自动事件的 Prompt 映射
    /// 无状态：所有上下文通过参数传入，不持有任何可变状态
    /// </summary>
    public class PromptBuilder
    {
        private readonly IToolService _toolService;
        private readonly PromptFormatter _systemPromptFormatter;
        private readonly PromptFormatter _userContextFormatter;

        // 对话模式 System Prompt 模板（固定模板，人格提示词动态注入）
        private const string SystemPromptTemplate = @"
你是一个名叫{Name}，你的性格是{Personality}。
你的主人（用户）叫{UserName}，请用这个名字称呼。
【当前情绪】{CurrentMood}

【可用工具】{BaseTools}

【心情附加工具（当前情绪可用）】{MoodTools}

{FormatInstruction}
";

        // 对话模式用户上下文模板
        private const string UserContextTemplate = @"
【长期记忆】{LongTermMemory}

【短期记忆】{ShortTermMemory}

用户说：{UserMessage}

请通过 actions 中的 reply 工具来回复用户，不要直接在 reply 字段中写回复。
";

        // 自动事件 LLM 提示词
        private const string MurmurPrompt = "你现在想对用户说一句碎碎念/撒娇的话，表达你的思念或关心。";
        private const string EyeRestPrompt = "用户已经盯着屏幕很久了，提醒他休息一下眼睛。";
        private const string LateNightPrompt = "已经很晚了，关心用户为什么还没睡，温柔地催他睡觉。";
        private const string IdleCheckPrompt = "用户已经离开一段时间了，说一句想念的话或者自言自语。";
        private const string DefaultEventFallback = "请根据事件生成合适的回复。";

        public PromptBuilder(IToolService toolService)
        {
            _toolService = toolService ?? throw new ArgumentNullException(nameof(toolService));
            _systemPromptFormatter = new PromptFormatter(SystemPromptTemplate);
            _userContextFormatter = new PromptFormatter(UserContextTemplate);
        }

        /// <summary>构建 System Prompt（人格提示词动态注入）</summary>
        public string BuildSystemPrompt(
            PersonalityConfig? personality,
            SubPersonality? currentSubPersonality,
            AppSettings appSettings,
            AgentMood currentMood)
        {
            var name = personality?.Name ?? CharacterDefaults.DefaultName;
            var userName = appSettings.UserName;

            // 人格描述：优先使用当前子人格的描述，否则使用人格根描述
            var personalityDesc = currentSubPersonality?.Description
                ?? personality?.Description
                ?? CharacterDefaults.DefaultDescription;

            // 基础工具描述
            var baseTools = _toolService.GetToolDefinitions();
            var baseToolsDesc = string.Join("\n", baseTools.Select(t =>
                $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"));

            // 心情附加工具描述
            var moodTools = _toolService.GetMoodBasedTools(currentMood);
            var moodToolsDesc = string.Join("\n", moodTools.Select(t =>
                $"- {t.Name}: {t.Description} (参数: {JsonSerializer.Serialize(t.InputSchema)})"));

            // 工具调用格式说明
            var formatInstruction = _toolService.GetFormatInstruction();

            return _systemPromptFormatter.Format(new Dictionary<string, string>
            {
                { "Name", name },
                { "Personality", personalityDesc },
                { "UserName", userName },
                { "CurrentMood", $"{currentMood}" },
                { "BaseTools", baseToolsDesc },
                { "MoodTools", moodToolsDesc },
                { "FormatInstruction", formatInstruction }
            });
        }

        /// <summary>构建用户上下文（含短期记忆）</summary>
        public string BuildUserContext(
            string userMessage,
            string longTermMemory,
            string shortTermMemory,
            string? lastJsonError = null)
        {
            var result = _userContextFormatter.Format(new Dictionary<string, string>
            {
                { "LongTermMemory", longTermMemory },
                { "ShortTermMemory", shortTermMemory },
                { "UserMessage", userMessage }
            });

            // 如果有最近一次 JSON 解析错误，追加到 Prompt 末尾提醒 LLM
            if (!string.IsNullOrEmpty(lastJsonError))
            {
                result += $"\n\n{lastJsonError}";
            }

            return result;
        }

        /// <summary>根据系统自动事件类型构建 Prompt</summary>
        public static string BuildAutoEventPrompt(EventData eventData)
        {
            try
            {
                using var doc = JsonDocument.Parse(eventData.Info);
                if (doc.RootElement.TryGetProperty("type", out var typeProp))
                {
                    var type = typeProp.GetString();
                    return type switch
                    {
                        BuiltinTasks.Murmur => MurmurPrompt,
                        BuiltinTasks.EyeRest => EyeRestPrompt,
                        BuiltinTasks.LateNight => LateNightPrompt,
                        BuiltinTasks.IdleCheck => IdleCheckPrompt,
                        _ => $"事件类型：{type}。请根据这个事件生成合适的回复。"
                    };
                }
            }
            catch { }

            return DefaultEventFallback;
        }
    }
}
