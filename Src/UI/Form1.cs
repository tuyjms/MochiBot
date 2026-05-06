using System.Text.Json;
using catgirlwindow.Src.Agent;
using catgirlwindow.Src.Core.Config;
using catgirlwindow.Src.Services;

namespace catgirlwindow.SrcUI
{
    public partial class Form1 : Form
    {
        private readonly LlmClient _llmClient = new();
        private IAgent _agent;
        private IShortTermMemory _shortTermMemory;
        private bool _isInitializing = true;

        public Form1()
        {
            InitializeComponent();

            // 初始化 ConfigReader（单例模式）
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            ConfigReader.Initialize(Path.Combine(baseDir, "Resources", "appsettings.json"));
            var configReader = ConfigReader.Instance;
            var shortTermMemory = new ShortTermMemory(50);
            var moodTracker = new AgentMoodTracker();
            var formatter = new PromptFormatter("");
            var toolService = new ToolService(_llmClient, moodTracker, formatter);

            // 保存短期记忆引用，用于提供商切换时保留对话历史
            _shortTermMemory = shortTermMemory;

            // 创建 Agent（不传中期/长期记忆）
            _agent = new MainAgent(
                _llmClient,
                configReader,
                formatter,
                shortTermMemory,
                toolService,
                moodTracker);

            InitializeProviders();
        }

        private void InitializeProviders()
        {
            comboBoxProvider.Items.AddRange(_llmClient.GetAvailableProviders().ToArray());
        }

        private async void buttonSend_Click(object sender, EventArgs e)
        {
            await SendMessage();
        }

        private void buttonClear_Click(object sender, EventArgs e)
        {
            richTextChat.Clear();
        }

        private void textBoxPrompt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _ = SendMessage();
            }
        }

        private async Task SendMessage()
        {
            var prompt = textBoxPrompt.Text.Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                return;
            }

            AppendChat("你", prompt);
            textBoxPrompt.Clear();
            buttonSend.Enabled = false;

            try
            {
                // 使用 Agent 处理用户输入
                var response = await _agent.ProcessUserInputAsync(prompt);
                // 从人格配置中获取角色名称
                var personality = ConfigReader.Instance.GetActivePersonality();
                var agentName = personality?.Name ?? "小琪";
                AppendChat(agentName, response);
            }
            catch (Exception ex)
            {
                AppendChat("错误", ex.Message);
            }
            finally
            {
                buttonSend.Enabled = true;
            }
        }

        private void comboBoxProvider_SelectedIndexChanged(object sender, EventArgs e)
        {
            // 初始化时跳过，避免在设置 SelectedIndex=0 时触发切换
            if (_isInitializing)
            {
                _isInitializing = false;
                return;
            }

            // TODO: 提供商切换功能待重构
            // 当前提供商由人格配置中的模型名决定（格式："{提供商}/{模型名}"）
            // 后续需要重构 UI 来支持修改人格配置中的模型
            if (comboBoxProvider.SelectedItem is string providerName)
            {
                AppendChat("系统", $"提供商切换功能暂不可用，当前使用人格配置中的模型。选中：{providerName}");
            }
        }

        private void AppendChat(string role, string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            richTextChat.AppendText($"[{role}] {content}\n\n");
            richTextChat.SelectionStart = richTextChat.Text.Length;
            richTextChat.ScrollToCaret();
        }
    }
}
