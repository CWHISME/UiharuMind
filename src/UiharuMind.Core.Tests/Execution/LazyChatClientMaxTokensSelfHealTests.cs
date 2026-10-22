using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// max_tokens 被服务端拒绝时:一次性把配置回退为服务端声明的合法上限、落盘并整段重发。
/// 替换了全局 <see cref="RemoteModelSettingConfig.Current"/>,不能与其它触碰它的测试并行。
/// </summary>
[Collection("RemoteModelSettingState")]
public class LazyChatClientMaxTokensSelfHealTests
{
    private const string MaxTokensErrorText =
        "HTTP 400 (invalid_request_error: 3)\nParameter: max_tokens\n\nfield MaxTokens invalid, should be in [1, 384000]";

    private static RemoteModelInfo NewRemoteWithOversizedMaxTokens()
    {
        return new RemoteModelInfo
        {
            Config = new RemoteAgnesModelConfig
            {
                ModelName = "agnes-self-heal-test",
                ModelId = "agnes-3.0-flash",
                MaxTokens = 1_000_000, // 远超服务端声明的 384000
            },
        };
    }

    [Fact]
    public async Task RejectedMaxTokens_CorrectsConfigAndRetriesOnce()
    {
        RemoteModelInfo remote = NewRemoteWithOversizedMaxTokens();
        RemoteModelSettingConfig previous = RemoteModelSettingConfig.Current;
        RemoteModelSettingConfig.Current = new RemoteModelSettingConfig
        {
            ModelInfos = { [remote.ModelName] = remote },
        };

        try
        {
            var fake = new MaxTokensFlakyClient(MaxTokensErrorText);
            var data = new ModelRunningData(remote);
            data.CompleteLoading(fake, loadToken: CancellationToken.None);
            var lazy = new LazyChatClient(() => data);

            List<string> texts = [];
            await foreach (ChatResponseUpdate update in lazy.GetStreamingResponseAsync(
                               [new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
            {
                foreach (AIContent content in update.Contents)
                {
                    if (content is TextContent text) texts.Add(text.Text);
                }
            }

            // 两次尝试:第一次被 400 拒,修好配置后整段重发;第二次成功
            Assert.Equal(2, fake.Calls);
            Assert.Equal(384000, remote.Config.MaxTokens);
            Assert.Equal(["ok"], texts);
        }
        finally
        {
            RemoteModelSettingConfig.Current = previous;
        }
    }

    [Fact]
    public async Task RejectedAgain_Throws_WithoutSecondSelfHeal()
    {
        RemoteModelInfo remote = NewRemoteWithOversizedMaxTokens();
        RemoteModelSettingConfig previous = RemoteModelSettingConfig.Current;
        RemoteModelSettingConfig.Current = new RemoteModelSettingConfig
        {
            ModelInfos = { [remote.ModelName] = remote },
        };

        try
        {
            var fake = new MaxTokensFlakyClient(MaxTokensErrorText, rejectAlways: true);
            var data = new ModelRunningData(remote);
            data.CompleteLoading(fake, loadToken: CancellationToken.None);
            var lazy = new LazyChatClient(() => data);

            await Assert.ThrowsAnyAsync<ClientResultException>(async () =>
            {
                await foreach (ChatResponseUpdate _ in lazy.GetStreamingResponseAsync(
                                   [new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
                {
                }
            });

            // 自愈只做一次:原始一次 + 自愈重发一次,第二次仍被拒就抛,不无限重试
            Assert.Equal(2, fake.Calls);
        }
        finally
        {
            RemoteModelSettingConfig.Current = previous;
        }
    }

    /// <summary>
    /// 前 <paramref name="rejectCount"/> 次调用抛服务端 400(max_tokens),之后正常吐一条回复
    /// </summary>
    private sealed class MaxTokensFlakyClient(string errorText, bool rejectAlways = false) : IChatClient
    {
        public int Calls;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref Calls);
            if (rejectAlways || call == 1)
            {
                await Task.Yield();
                throw new ClientResultException(errorText);
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}