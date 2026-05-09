using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using EventTrigger = MochiBot.Src.EventModels.EventTrigger;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 聊天窗口 - 类似即时通讯软件的独立聊天界面
    /// </summary>
    public partial class ChatWindow : Window
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;
        private readonly ObservableCollection<ChatMessageItem> _messages = new();
        private string? _replySubscriptionId;

        /// <summary>
        /// 最新一条 agent 消息，供主窗口气泡显示
        /// </summary>
        public string? LatestAgentMessage { get; private set; }

        /// <summary>
        /// 有新消息时触发，通知主窗口更新气泡
        /// </summary>
        public event Action? NewAgentMessage;

        public ChatWindow(IEventDispatcher eventDispatcher, IConfigReader configReader)
        {
            _eventDispatcher = eventDispatcher;
            _configReader = configReader;

            InitializeComponent();

            messageList.ItemsSource = _messages;

            // 订阅 agent 回复事件
            SubscribeToReplyEvents();
        }

        private void SubscribeToReplyEvents()
        {
            try
            {
                if (_eventDispatcher != null)
                {
                    _replySubscriptionId = _eventDispatcher.Subscribe(EventCategory.ToolResult, (eventData) =>
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(eventData.Info);
                            if (doc.RootElement.TryGetProperty("type", out var typeProp))
                            {
                                var type = typeProp.GetString();
                                if (type == "reply")
                                {
                                    var content = doc.RootElement.TryGetProperty("content", out var contentProp)
                                        ? contentProp.GetString() ?? ""
                                        : "";
                                    if (!string.IsNullOrEmpty(content))
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            AddAgentMessage(content);
                                        });
                                    }
                                }
                            }
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }

        /// <summary>
        /// 添加 agent 消息
        /// </summary>
        private void AddAgentMessage(string text)
        {
            _messages.Add(new ChatMessageItem
            {
                Text = text,
                IsUser = false,
                Alignment = HorizontalAlignment.Left
            });

            LatestAgentMessage = text;
            NewAgentMessage?.Invoke();

            ScrollToBottom();
        }

        /// <summary>
        /// 添加用户消息
        /// </summary>
        private void AddUserMessage(string text)
        {
            _messages.Add(new ChatMessageItem
            {
                Text = text,
                IsUser = true,
                Alignment = HorizontalAlignment.Right
            });

            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (messageList.Items.Count > 0)
            {
                messageList.ScrollIntoView(messageList.Items[messageList.Items.Count - 1]);
            }
        }

        private void SendMessage()
        {
            var text = inputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            AddUserMessage(text);
            inputBox.Clear();

            // 发布用户输入事件，触发 Agent 处理
            if (_eventDispatcher != null)
            {
                _eventDispatcher.Publish(new EventData
                {
                    Category = EventCategory.UserInput,
                    Trigger = EventTrigger.User,
                    Info = text
                });
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        /// <summary>
        /// 窗口关闭时隐藏而非销毁，保持聊天记录
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }
    }
}
