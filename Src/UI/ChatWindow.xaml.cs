using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using static MochiBot.Src.Core.Constants;
using EventTrigger1 = MochiBot.Src.EventModels.EventTrigger;

namespace MochiBot.Src.UI
{
    /// <summary>
    /// 聊天窗口 - 类似即时通讯软件的独立聊天界面
    /// </summary>
    public partial class ChatWindow : Window
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;
        private readonly ChatHistoryRepository _chatHistoryRepo;
        private readonly ObservableCollection<ChatMessageItem> _messages = new();
        private string? _replySubscriptionId;
        private readonly DispatcherTimer _searchDebounceTimer;
        private bool _isSearchMode;

        /// <summary>
        /// 最新一条 agent 消息，供主窗口气泡显示
        /// </summary>
        public string? LatestAgentMessage { get; private set; }

        /// <summary>
        /// 有新消息时触发，通知主窗口更新气泡
        /// </summary>
        public event Action? NewAgentMessage;

        public ChatWindow(IEventDispatcher eventDispatcher, IConfigReader configReader, ChatHistoryRepository chatHistoryRepo)
        {
            _eventDispatcher = eventDispatcher;
            _configReader = configReader;
            _chatHistoryRepo = chatHistoryRepo;

            // 搜索防抖定时器
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            InitializeComponent();

            messageList.ItemsSource = _messages;

            // 从配置读取角色名称
            LoadCharacterName();

            // 订阅 agent 回复事件
            SubscribeToReplyEvents();

            // 从数据库加载历史聊天记录
            _ = LoadHistoryAsync();
        }

        /// <summary>
        /// 从配置读取角色名称并更新 UI
        /// </summary>
        private void LoadCharacterName()
        {
            var personality = _configReader.GetActivePersonality();
            var characterName = personality?.Name ?? CharacterDefaults.DefaultName;

            // 更新窗口标题
            Title = characterName;

            // 更新标题栏中的名称和头像文字
            if (FindName("characterNameText") is System.Windows.Controls.TextBlock nameText)
                nameText.Text = characterName;

            if (FindName("characterAvatarText") is System.Windows.Controls.TextBlock avatarText)
                avatarText.Text = characterName.Length > 0 ? characterName[..1] : CharacterDefaults.DefaultAvatarText;
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
                                if (type == Tools.Reply)
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
                    Trigger = EventTrigger1.User,
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        /// <summary>
        /// 从数据库加载历史聊天记录到 UI
        /// </summary>
        private async Task LoadHistoryAsync()
        {
            try
            {
                var history = await _chatHistoryRepo.LoadChatHistoryWithIdAsync(limit: 100);
                if (history.Count == 0) return;

                foreach (var (id, msg) in history)
                {
                    _messages.Add(new ChatMessageItem
                    {
                        Id = id,
                        Text = msg.Content,
                        IsUser = msg.Role == ChatRoles.User,
                        Alignment = msg.Role == ChatRoles.User
                            ? HorizontalAlignment.Right
                            : HorizontalAlignment.Left
                    });
                }

                ScrollToBottom();
            }
            catch
            {
                // 加载历史失败不影响正常使用
            }
        }

        // ========== 搜索功能 ==========

        private void SearchToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearchMode)
            {
                CloseSearchPanel();
            }
            else
            {
                OpenSearchPanel();
            }
        }

        private void OpenSearchPanel()
        {
            _isSearchMode = true;
            searchPanel.Visibility = Visibility.Visible;
            searchBox.Focus();
        }

        private void CloseSearchPanel()
        {
            _isSearchMode = false;
            searchPanel.Visibility = Visibility.Collapsed;
            _searchDebounceTimer.Stop();
            searchBox.Text = "";
            // 恢复完整列表
            _ = RestoreFullListAsync();
        }

        private void SearchCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSearchPanel();
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // 重置防抖定时器
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseSearchPanel();
                e.Handled = true;
            }
        }

        private async void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            var keyword = searchBox.Text.Trim();
            if (string.IsNullOrEmpty(keyword)) return;

            try
            {
                var results = await _chatHistoryRepo.SearchMessagesAsync(keyword);
                _messages.Clear();
                foreach (var (id, msg) in results)
                {
                    _messages.Add(new ChatMessageItem
                    {
                        Id = id,
                        Text = msg.Content,
                        IsUser = msg.Role == ChatRoles.User,
                        Alignment = msg.Role == ChatRoles.User
                            ? HorizontalAlignment.Right
                            : HorizontalAlignment.Left
                    });
                }
            }
            catch
            {
                // 搜索失败静默处理
            }
        }

        private async Task RestoreFullListAsync()
        {
            _messages.Clear();
            await LoadHistoryAsync();
        }

        // ========== 删除功能 ==========

        private async void DeleteMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: int id } || id <= 0) return;

            try
            {
                await _chatHistoryRepo.DeleteMessageByIdAsync(id);
                var item = _messages.FirstOrDefault(m => m.Id == id);
                if (item != null)
                    _messages.Remove(item);
            }
            catch
            {
                // 删除失败静默处理
            }
        }

        // ========== 清空功能 ==========

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_messages.Count == 0) return;

            var result = System.Windows.MessageBox.Show(
                "确定要清空所有聊天记录吗？此操作不可撤销。",
                "确认清空",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _chatHistoryRepo.DeleteAllMessagesAsync();
                _messages.Clear();

                // 如果在搜索模式，退出搜索
                if (_isSearchMode)
                {
                    _isSearchMode = false;
                    searchPanel.Visibility = Visibility.Collapsed;
                    _searchDebounceTimer.Stop();
                    searchBox.Text = "";
                }
            }
            catch
            {
                // 清空失败静默处理
            }
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
