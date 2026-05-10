using MochiBot.Src.Core.Config;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 短期记忆 - 环形缓冲区实现，固定容量，自动淘汰旧记录
    /// </summary>
    public class ShortTermMemory : IShortTermMemory
    {
        private ChatMessage[] _buffer;
        private int _head;
        private int _count;
        private int _capacity;
        private LlmClient? _llmClient;
        private OverflowStrategy _overflowStrategy = OverflowStrategy.Truncate;
        private string? _contextSummary;
        private readonly IConfigReader _configReader;

        private const int DefaultCapacity = 50;
        private const int SummaryReservedCount = 10;

        public ShortTermMemory(int capacity, string provider, string model, IConfigReader configReader)
        {
            _configReader = configReader;
            _capacity = capacity > 0 ? capacity : DefaultCapacity;
            _buffer = new ChatMessage[_capacity];
            _head = 0;
            _count = 0;
            _llmClient = new LlmClient(provider, model, configReader);
        }

        /// <summary>用于单元测试的构造函数（不创建 LlmClient）</summary>
        public ShortTermMemory(int capacity = DefaultCapacity)
        {
            _configReader = ConfigReader.Instance;
            _capacity = capacity > 0 ? capacity : DefaultCapacity;
            _buffer = new ChatMessage[_capacity];
            _head = 0;
            _count = 0;
        }

        public int Count => _count;
        public int Capacity
        {
            get => _capacity;
            set
            {
                if (value <= 0) return;
                var oldMessages = GetAllMessages();
                _capacity = value;
                _buffer = new ChatMessage[_capacity];
                _head = 0;
                _count = 0;

                // 重新填充，保留最近的 N 条
                var startIndex = Math.Max(0, oldMessages.Count - _capacity);
                for (int i = startIndex; i < oldMessages.Count; i++)
                {
                    AddMessage(oldMessages[i].Role, oldMessages[i].Content);
                }
            }
        }

        public OverflowStrategy OverflowStrategy
        {
            get => _overflowStrategy;
            set => _overflowStrategy = value;
        }

        public string? ContextSummary => _contextSummary;

        public void AddMessage(string role, string content)
        {
            var message = new ChatMessage
            {
                Role = role,
                Content = content,
                Timestamp = DateTime.Now
            };

            if (_count < _capacity)
            {
                // 缓冲区未满，直接添加
                var index = (_head + _count) % _capacity;
                _buffer[index] = message;
                _count++;
            }
            else
            {
                // 缓冲区已满，根据策略处理
                if (_overflowStrategy == OverflowStrategy.Truncate)
                {
                    // 覆盖最旧的消息
                    _buffer[_head] = message;
                    _head = (_head + 1) % _capacity;
                }
                else if (_overflowStrategy == OverflowStrategy.Summarize)
                {
                    // Summarize 策略：需要外部调用 SummarizeAsync 处理
                    // 这里先按 Truncate 方式添加，由外部在适当时机调用 SummarizeAsync
                    _buffer[_head] = message;
                    _head = (_head + 1) % _capacity;
                }
            }
        }

        public List<ChatMessage> GetRecentMessages(int count = 10)
        {
            if (count <= 0) return new List<ChatMessage>();
            if (count > _count) count = _count;

            var result = new List<ChatMessage>(count);
            var startIndex = (_head + _count - count) % _capacity;

            for (int i = 0; i < count; i++)
            {
                var index = (startIndex + i) % _capacity;
                if (_buffer[index] != null)
                    result.Add(_buffer[index]);
            }

            return result;
        }

        public List<ChatMessage> GetAllMessages()
        {
            var result = new List<ChatMessage>(_count);
            for (int i = 0; i < _count; i++)
            {
                var index = (_head + i) % _capacity;
                if (_buffer[index] != null)
                    result.Add(_buffer[index]);
            }
            return result;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
            _contextSummary = null;
        }

        public async Task<string> SummarizeAsync()
        {
            var messagesToSummarize = new List<ChatMessage>();
            var reservedMessages = new List<ChatMessage>();

            var allMessages = GetAllMessages();
            var splitIndex = Math.Max(0, allMessages.Count - SummaryReservedCount);

            for (int i = 0; i < allMessages.Count; i++)
            {
                if (i < splitIndex)
                    messagesToSummarize.Add(allMessages[i]);
                else
                    reservedMessages.Add(allMessages[i]);
            }

            // 构建总结文本
            var chatHistory = string.Join("\n",
                messagesToSummarize.Select(m => $"{m.Role}: {m.Content}"));

            string summary;

            // 如果有 LlmClient，使用 LLM 进行真实总结
            if (_llmClient != null && messagesToSummarize.Count > 0)
            {
                try
                {
                    var prompt = $"请总结以下对话的核心内容，提取关键信息（用户偏好、重要事件、约定等）：\n\n{chatHistory}";
                    summary = await _llmClient.SendChatAsync(prompt);
                    _configReader.Logger.Info($"[ShortTermMemory] LLM 总结完成: {summary[..Math.Min(summary.Length, 100)]}...");
                }
                catch (Exception ex)
                {
                    _configReader.Logger.Warn($"[ShortTermMemory] LLM 总结失败，使用默认摘要: {ex.Message}");
                    summary = $"对话摘要：共 {messagesToSummarize.Count} 条消息，涉及 {messagesToSummarize.Select(m => m.Role).Distinct().Count()} 个角色。";
                }
            }
            else
            {
                // 没有 LlmClient 时使用默认摘要
                summary = $"对话摘要：共 {messagesToSummarize.Count} 条消息，涉及 {messagesToSummarize.Select(m => m.Role).Distinct().Count()} 个角色。";
            }

            // 重建缓冲区：摘要(system角色) + 保留的最近消息
            Clear();
            _contextSummary = summary;
            AddMessage("system", summary);
            foreach (var msg in reservedMessages)
            {
                AddMessage(msg.Role, msg.Content);
            }

            return summary;
        }
    }
}
