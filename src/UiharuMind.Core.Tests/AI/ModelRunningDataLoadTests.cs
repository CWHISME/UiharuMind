using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 加载被停掉或被新一次加载接手后，旧那次的收尾不能动新状态——否则新进程的取消源丢了，会无主驻留
/// </summary>
public class ModelRunningDataLoadTests
{
    private static ModelRunningData Local() => new(new GGufModelInfo { ModelName = "m" });

    [Fact]
    public void StaleFailLoading_LeavesNewLoadIntact()
    {
        ModelRunningData model = Local();
        CancellationToken first = model.BeginLoading();
        model.StopRunning();
        CancellationToken second = model.BeginLoading();

        model.FailLoading(new InvalidOperationException("old"), first);

        Assert.True(model.IsLoading);
        Assert.Null(model.LastError);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void StaleCompleteLoading_DisposesClientAndStaysUnloaded()
    {
        ModelRunningData model = Local();
        CancellationToken first = model.BeginLoading();
        model.StopRunning();
        DisposableClient client = new();

        bool installed = model.CompleteLoading(client, loadToken: first);

        Assert.False(installed);
        Assert.True(client.Disposed);
        Assert.Null(model.ChatClient);
    }

    [Fact]
    public void CurrentLoad_CompletesNormally()
    {
        ModelRunningData model = Local();
        CancellationToken token = model.BeginLoading();

        Assert.True(model.CompleteLoading(new DisposableClient(), loadToken: token));
        Assert.True(model.IsRunning);
        Assert.False(model.IsLoading);
    }

    private sealed class DisposableClient : IChatClient
    {
        public bool Disposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Disposed = true;
    }
}
