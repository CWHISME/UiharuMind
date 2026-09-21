using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using static UiharuMind.Core.AI.Execution.LazyChatClient;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 会话钉选候选的处置决策。钉选命中但从未启动（全局未选）时：
/// 远程用它（拉起后等就绪），本地未起回落全局，回无可回就地报错、不空等 30 秒。
/// 纯函数，不碰任何单例。
/// </summary>
public class SessionModelResolutionTests
{
    private static ModelRunningData StoppedLocal(string name) =>
        new(new GGufModelInfo { ModelName = name });

    private static ModelRunningData Remote(string name) =>
        new(new RemoteModelInfo());

    [Fact]
    public void RunningCandidate_UsesIt()
    {
        ModelRunningData running = StoppedLocal("m1");
        running.CompleteLoading(new StubChatClient());

        Assert.Equal(SessionCandidateDecision.UseCandidate,
            DecideCandidate(running, StoppedLocal("m2")));
    }

    [Fact]
    public void LoadingCandidate_UsesIt()
    {
        ModelRunningData loading = StoppedLocal("m1");
        loading.BeginLoading();

        Assert.Equal(SessionCandidateDecision.UseCandidate,
            DecideCandidate(loading, null));
    }

    [Fact]
    public void RemoteStopped_UsesIt()
    {
        Assert.Equal(SessionCandidateDecision.UseCandidate,
            DecideCandidate(Remote("m1"), null));
    }

    [Fact]
    public void LocalStopped_WithOtherGlobal_UsesGlobal()
    {
        Assert.Equal(SessionCandidateDecision.UseGlobal,
            DecideCandidate(StoppedLocal("m1"), StoppedLocal("m2")));
    }

    [Fact]
    public void LocalStopped_WithNullGlobal_FailsFast()
    {
        Assert.Equal(SessionCandidateDecision.FailFast,
            DecideCandidate(StoppedLocal("m1"), null));
    }

    [Fact]
    public void LocalStopped_WithSameGlobal_FailsFast()
    {
        ModelRunningData same = StoppedLocal("m1");

        Assert.Equal(SessionCandidateDecision.FailFast,
            DecideCandidate(same, same));
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
