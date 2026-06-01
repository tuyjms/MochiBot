using MochiBot.Src.Core.Config;
using MochiBot.Src.EventModels;
using MochiBot.Src.Services;
using MochiBot.Src.Services.Tool;

namespace MochiBot.Tests;

[Collection("ConfigReader")]
public class ToolServiceTests : IDisposable
{
    private readonly ToolService _toolService;

    public ToolServiceTests()
    {
        TestConfigHelper.EnsureInitialized();
        var configReader = ConfigReader.Instance;
        _toolService = new ToolService(configReader, modLoader: null);
    }

    public void Dispose()
    {
        _toolService.Dispose();
    }

    // ========== 工具定义 ==========

    [Fact]
    public void GetToolDefinitions_Default_ReturnsBaseTools()
    {
        var tools = _toolService.GetToolDefinitions();
        var names = tools.Select(t => t.Name).ToList();

        Assert.Contains("timer", names);
        Assert.Contains("reply", names);
        Assert.Contains("list_plugins", names);
    }

    [Fact]
    public void GetToolDefinitions_Default_EachToolHasDescription()
    {
        var tools = _toolService.GetToolDefinitions();

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrEmpty(tool.Name));
            Assert.False(string.IsNullOrEmpty(tool.Description));
            Assert.NotNull(tool.InputSchema);
        }
    }

    // ========== 心情附加工具 ==========

    [Fact]
    public void GetMoodBasedTools_Sad_ReturnsCryTool()
    {
        var tools = _toolService.GetMoodBasedTools(AgentMood.Sad);

        Assert.Single(tools);
        Assert.Equal("cry", tools[0].Name);
    }

    [Fact]
    public void GetMoodBasedTools_Happy_ReturnsDanceTool()
    {
        var tools = _toolService.GetMoodBasedTools(AgentMood.Happy);

        Assert.Single(tools);
        Assert.Equal("dance", tools[0].Name);
    }

    [Fact]
    public void GetMoodBasedTools_Sleepy_ReturnsYawnTool()
    {
        var tools = _toolService.GetMoodBasedTools(AgentMood.Sleepy);

        Assert.Single(tools);
        Assert.Equal("yawn", tools[0].Name);
    }

    [Fact]
    public void GetMoodBasedTools_Touched_ReturnsBlushTool()
    {
        var tools = _toolService.GetMoodBasedTools(AgentMood.Touched);

        Assert.Single(tools);
        Assert.Equal("blush", tools[0].Name);
    }

    [Fact]
    public void GetMoodBasedTools_Angry_ReturnsStompTool()
    {
        var tools = _toolService.GetMoodBasedTools(AgentMood.Angry);

        Assert.Single(tools);
        Assert.Equal("stomp", tools[0].Name);
    }

    [Fact]
    public void GetMoodBasedTools_Neutral_ReturnsEmptyList()
    {
        var tools = _toolService.GetMoodBasedTools(AgentMood.Neutral);

        Assert.Empty(tools);
    }

    [Fact]
    public void GetMoodBasedTools_Surprised_ReturnsEmptyList()
    {
        var tools = _toolService.GetMoodBasedTools(AgentMood.Surprised);

        Assert.Empty(tools);
    }

    [Fact]
    public void GetMoodBasedTools_Teasing_ReturnsEmptyList()
    {
        var tools = _toolService.GetMoodBasedTools(AgentMood.Teasing);

        Assert.Empty(tools);
    }

    // ========== 执行基础工具 ==========

    [Fact]
    public async Task ExecuteToolAsync_Timer_ReturnsSuccess()
    {
        var result = await _toolService.ExecuteToolAsync("timer", "{\"seconds\": 10}");

        Assert.True(result.Success);
        Assert.Contains("10", result.Data);
    }

    [Fact]
    public async Task ExecuteToolAsync_Timer_MissingSeconds_ReturnsFailure()
    {
        var result = await _toolService.ExecuteToolAsync("timer", "{}");

        Assert.False(result.Success);
        Assert.Contains("seconds", result.Error ?? "");
    }

    [Fact]
    public async Task ExecuteToolAsync_Timer_InvalidJson_ReturnsFailure()
    {
        var result = await _toolService.ExecuteToolAsync("timer", "not-json");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteToolAsync_Murmur_ReturnsMurmurText()
    {
        var result = await _toolService.ExecuteToolAsync("murmur", "");

        Assert.True(result.Success);
        Assert.Contains("murmur", result.Data);
    }

    [Fact]
    public async Task ExecuteToolAsync_ListPlugins_ReturnsPluginsList()
    {
        var result = await _toolService.ExecuteToolAsync("list_plugins", "");

        Assert.True(result.Success);
        Assert.Contains("plugins", result.Data);
        Assert.Contains("mcp_tools", result.Data);
    }

    // ========== 执行心情工具 ==========

    [Fact]
    public async Task ExecuteToolAsync_Cry_ReturnsAnimation()
    {
        var result = await _toolService.ExecuteToolAsync("cry", "");

        Assert.True(result.Success);
        Assert.Contains("animation", result.Data);
        Assert.Contains("cry", result.Data);
    }

    [Fact]
    public async Task ExecuteToolAsync_Dance_ReturnsAnimation()
    {
        var result = await _toolService.ExecuteToolAsync("dance", "");

        Assert.True(result.Success);
        Assert.Contains("animation", result.Data);
        Assert.Contains("dance", result.Data);
    }

    [Fact]
    public async Task ExecuteToolAsync_Yawn_ReturnsAnimation()
    {
        var result = await _toolService.ExecuteToolAsync("yawn", "");

        Assert.True(result.Success);
        Assert.Contains("yawn", result.Data);
    }

    [Fact]
    public async Task ExecuteToolAsync_Blush_ReturnsAnimation()
    {
        var result = await _toolService.ExecuteToolAsync("blush", "");

        Assert.True(result.Success);
        Assert.Contains("blush", result.Data);
    }

    [Fact]
    public async Task ExecuteToolAsync_Stomp_ReturnsAnimation()
    {
        var result = await _toolService.ExecuteToolAsync("stomp", "");

        Assert.True(result.Success);
        Assert.Contains("stomp", result.Data);
    }

    // ========== 不存在的工具 ==========

    [Fact]
    public async Task ExecuteToolAsync_Nonexistent_ReturnsFailure()
    {
        var result = await _toolService.ExecuteToolAsync("nonexistent_tool", "");

        Assert.False(result.Success);
        Assert.Contains("nonexistent_tool", result.Error ?? "");
    }

    // ========== 其他接口 ==========

    [Fact]
    public async Task ListPluginsAsync_NoModsLoaded_ReturnsEmptyList()
    {
        var tools = await _toolService.ListPluginsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public async Task ListMcpToolsAsync_Default_ReturnsEmptyList()
    {
        var tools = await _toolService.ListMcpToolsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public void GetFormatInstruction_Default_ContainsActionTypeDescriptions()
    {
        var instruction = _toolService.GetFormatInstruction();

        Assert.Contains("tool_call", instruction);
        Assert.Contains("plugin_call", instruction);
        Assert.Contains("mcp_call", instruction);
        Assert.Contains("mood_change", instruction);
    }

    // ========== 计时器状态 ==========

    [Fact]
    public void GetTimerStatus_Default_ReturnsIdle()
    {
        Assert.Equal(TimerStatus.Idle, _toolService.GetTimerStatus());
    }

    [Fact]
    public void GetTimerRemaining_Default_ReturnsZero()
    {
        Assert.Equal(0, _toolService.GetTimerRemaining());
    }
}
