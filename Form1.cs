using catgirlwindow.Services;
using catgirlwindow.Services.Config;

namespace catgirlwindow;

public partial class Form1 : Form
{
    private readonly LlmClient _llmClient = new();
    private readonly IAgent _agent;

    public Form1()
    {
        InitializeComponent();

        // 初始化 Agent 依赖
        var configReader = new ConfigReader();
        var shortTermMemory = new ShortTermMemory(50);
        var moodTracker = new AgentMoodTracker();
        var formatter = new PromptFormatter("");
        var toolService = new ToolService(_llmClient, moodTracker, formatter);

        // 创建 Agent（不传中期/长期记忆）
        _agent = new Agent(
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
        comboBoxProvider.SelectedIndex = 0;
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
            AppendChat("小琪", response);
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
