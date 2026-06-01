using MochiBot.Src.Core.Config;
using MochiBot.Src.Services;
using OpenAI.Chat;

namespace MochiBot.Tests.Services;

[Collection("ConfigReader")]
public class LlmClientTests : IDisposable
{
    public LlmClientTests()
    {
        TestConfigHelper.EnsureInitialized();
    }

    public void Dispose() { }

    // ========== 测试用子类 ==========

    /// <summary>
    /// 可控的 LlmClient 子类，通过 Func 委托模拟 LLM 调用行为
    /// 重写 CallLlmAsync 以拦截实际网络请求，保留重试逻辑完整
    /// </summary>
    private class TestableLlmClient : LlmClient
    {
        private readonly Func<List<ChatMessage>, Task<string>> _callFunc;

        public TestableLlmClient(Func<List<ChatMessage>, Task<string>> callFunc)
            : base("LocalLMStudio", "test-model", ConfigReader.Instance)
        {
            _callFunc = callFunc;
        }

        protected override async Task<string> CallLlmAsync(List<ChatMessage> messages)
        {
            return await _callFunc(messages);
        }
    }

    // ========== 重试成功 ==========

    [Fact]
    public async Task SendChatAsync_TransientFailuresThenSuccess_EventuallySucceeds()
    {
        int callCount = 0;
        var client = new TestableLlmClient(_ =>
        {
            callCount++;
            if (callCount <= 2)
                throw new HttpRequestException("网络抖动");
            return Task.FromResult("成功回复");
        });

        var result = await client.SendChatAsync("测试");

        Assert.Equal("成功回复", result);
        Assert.Equal(3, callCount); // 2 次失败 + 1 次成功
    }

    [Fact]
    public async Task SendChatAsync_FirstAttemptSucceeds_NoRetry()
    {
        int callCount = 0;
        var client = new TestableLlmClient(_ =>
        {
            callCount++;
            return Task.FromResult("直接成功");
        });

        var result = await client.SendChatAsync("测试");

        Assert.Equal("直接成功", result);
        Assert.Equal(1, callCount);
    }

    // ========== 重试耗尽 ==========

    [Fact]
    public async Task SendChatAsync_AllRetriesExhausted_ThrowsInvalidOperation()
    {
        var client = new TestableLlmClient(_ =>
        {
            throw new HttpRequestException("持续网络错误");
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendChatAsync("测试"));

        Assert.Contains("次尝试均失败", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    // ========== 非瞬态异常不重试 ==========

    [Fact]
    public async Task SendChatAsync_NonTransientException_DoesNotRetry()
    {
        int callCount = 0;
        var client = new TestableLlmClient(_ =>
        {
            callCount++;
            throw new UnauthorizedAccessException("认证失败");
        });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.SendChatAsync("测试"));

        Assert.Equal(1, callCount); // 只调用一次，不重试
    }

    // ========== TaskCanceledException (超时) 重试 ==========

    [Fact]
    public async Task SendChatAsync_TimeoutException_Retries()
    {
        int callCount = 0;
        var client = new TestableLlmClient(_ =>
        {
            callCount++;
            if (callCount <= 1)
                throw new TaskCanceledException("请求超时");
            return Task.FromResult("超时后恢复");
        });

        var result = await client.SendChatAsync("测试");

        Assert.Equal("超时后恢复", result);
        Assert.Equal(2, callCount);
    }

    // ========== SocketException 重试 ==========

    [Fact]
    public async Task SendChatAsync_SocketException_Retries()
    {
        int callCount = 0;
        var client = new TestableLlmClient(_ =>
        {
            callCount++;
            if (callCount <= 1)
                throw new System.Net.Sockets.SocketException(10054); // 连接被重置
            return Task.FromResult("网络恢复");
        });

        var result = await client.SendChatAsync("测试");

        Assert.Equal("网络恢复", result);
        Assert.Equal(2, callCount);
    }

    // ========== ProviderConfig 默认值 ==========

    [Fact]
    public void ProviderConfig_DefaultValues_AreCorrect()
    {
        var config = new Src.Core.Config.Models.ProviderConfig();

        Assert.Equal(30, config.TimeoutSeconds);
        Assert.Equal(3, config.MaxRetries);
        Assert.Equal(1000, config.RetryDelayMs);
    }

    // ========== SendChatAsync(string) 也经过重试 ==========

    [Fact]
    public async Task SendChatAsync_StringOverload_GoesThroughRetry()
    {
        int callCount = 0;
        var client = new TestableLlmClient(_ =>
        {
            callCount++;
            if (callCount <= 1)
                throw new HttpRequestException("网络抖动");
            return Task.FromResult("重试成功");
        });

        var result = await client.SendChatAsync("单条消息");

        Assert.Equal("重试成功", result);
        Assert.Equal(2, callCount);
    }
}
