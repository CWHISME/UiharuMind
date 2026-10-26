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
        running.CompleteLoading(new StubChatClient(), loadToken: CancellationToken.None);

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

    //================= ServingModel：真正会发请求的那个 =================
    // 旁路请求（写交接文档）要用它定上下文上限。钉着没在跑的本地模型时，
    // 钉着的那个的 ContextLength 会落到本地兜底 8192，而请求实际发给全局那个

    [Fact]
    public void ServingModel_StoppedLocal_WithRunningGlobal_ReturnsGlobal()
    {
        // 报过的缺陷：全局跑着 B、会话钉着没在跑的 A，压缩永远发不出去，
        // 每轮都报"记忆压缩没能完成"——而同一轮的常规请求走的就是 B
        ModelRunningData global = StoppedLocal("m2");
        global.CompleteLoading(new StubChatClient(), loadToken: CancellationToken.None);

        Assert.Same(global, ServingModel(StoppedLocal("m1"), global));
    }

    [Fact]
    public void ServingModel_RunningCandidate_ReturnsCandidate()
    {
        ModelRunningData running = StoppedLocal("m1");
        running.CompleteLoading(new StubChatClient(), loadToken: CancellationToken.None);

        Assert.Same(running, ServingModel(running, StoppedLocal("m2")));
    }

    [Fact]
    public void ServingModel_RemoteStopped_ReturnsCandidate()
    {
        // 远程没启动不算没得用：请求会把它拉起来（EnsureModelStarted）
        ModelRunningData remote = Remote("m1");

        Assert.Same(remote, ServingModel(remote, StoppedLocal("m2")));
    }

    [Fact]
    public void ServingModel_NoCandidate_ReturnsGlobal()
    {
        ModelRunningData global = StoppedLocal("m2");

        Assert.Same(global, ServingModel(null, global));
        Assert.Null(ServingModel(null, null));
    }

    [Fact]
    public void ServingModel_StoppedLocal_WithNullGlobal_ReturnsCandidate()
    {
        // 回无可回时仍把它交出去：调用方据 ChatClient 判"一个都指望不上"，
        // 判定只留一处，不在这里提前变成 null
        ModelRunningData stopped = StoppedLocal("m1");

        Assert.Same(stopped, ServingModel(stopped, null));
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
