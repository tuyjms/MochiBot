using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace catgirlwindow;

public partial class Form1 : Form
{
    private readonly LlmClient _llmClient = new();

    public Form1()
    {
        InitializeComponent();
        InitializeProviders();
    }

    private void InitializeProviders()
    {
        comboBoxProvider.Items.AddRange(_llmClient.GetAvailableProviders().ToArray());
        comboBoxProvider.SelectedIndex = 0; // Default to first provider
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

        var provider = comboBoxProvider.SelectedItem?.ToString() ?? "LocalLMStudio";
        var model = textBoxModel.Text.Trim();

        AppendChat("User", prompt);
        textBoxPrompt.Clear();
        buttonSend.Enabled = false;

        try
        {
            var response = await _llmClient.SendChatAsync(provider, model, prompt);
            AppendChat("Assistant", response);
        }
        catch (Exception ex)
        {
            AppendChat("Error", ex.Message);
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
