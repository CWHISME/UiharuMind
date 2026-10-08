using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 本地引擎进程宿主：并发共用一次启动、取消只放弃等待、停掉后等待方改等新的、连着失败就冷却、闲置自动停
/// </summary>
public class LocalServerHostTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-host-{Guid.NewGuid():N}");
    private readonly string _counter;

    public LocalServerHostTests()
    {
        Directory.CreateDirectory(_directory);
        _counter = Path.Combine(_directory, "starts");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task ConcurrentRequests_StartOneProcess()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using LocalServerHost host = Host("sleep 0.5");

        Uri[] uris = await Task.WhenAll(
            host.GetBaseUriAsync(CancellationToken.None), host.GetBaseUriAsync(CancellationToken.None));

        Assert.Equal(uris[0], uris[1]);
        Assert.Equal(1, Starts());
    }

    [Fact]
    public async Task CancelledRequest_DoesNotKillTheServer()
    {
        // 用户打断一次朗读不能把下一次朗读拖进几秒的冷启动
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using LocalServerHost host = Host("sleep 0.5");

        using CancellationTokenSource cancelled = new(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.GetBaseUriAsync(cancelled.Token));
        await host.GetBaseUriAsync(CancellationToken.None);
        await host.GetBaseUriAsync(CancellationToken.None);

        Assert.Equal(1, Starts());
    }

    [Fact]
    public async Task Stop_StartsAFreshProcess()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using LocalServerHost host = Host("");

        await host.GetBaseUriAsync(CancellationToken.None);
        host.Stop();
        await host.GetBaseUriAsync(CancellationToken.None);

        Assert.Equal(2, Starts());
    }

    [Fact]
    public async Task StopWhileStarting_WaiterGetsTheNewServer()
    {
        // 换模型时正在等的那句朗读不能当成被用户取消、悄悄不出声
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using LocalServerHost host = Host("sleep 1");

        Task<Uri> waiting = host.GetBaseUriAsync(CancellationToken.None);
        for (int i = 0; i < 100 && Starts() == 0; i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        host.Stop();

        Uri uri = await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(uri, host.RunningBaseUri);
        Assert.Equal(2, Starts());
    }

    [Fact]
    public async Task KeepsFailing_CoolsDownWithTheLastError_UntilStopped()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using LocalServerHost host = Host("echo 'model is broken' >&2\nexit 1");

        for (int i = 0; i < LocalServerHost.FailuresBeforeCooldown; i++)
            await Assert.ThrowsAsync<LocalServerException>(() => host.GetBaseUriAsync(CancellationToken.None));

        LocalServerException cooling =
            await Assert.ThrowsAsync<LocalServerException>(() => host.GetBaseUriAsync(CancellationToken.None));
        Assert.Contains("model is broken", cooling.LogTail);
        Assert.Equal(LocalServerHost.FailuresBeforeCooldown, Starts());
        Assert.Equal(EEngineRunState.Failed, host.State);
        Assert.NotNull(host.LastError);

        host.Stop(); //改了设置就该让它马上再试
        await Assert.ThrowsAsync<LocalServerException>(() => host.GetBaseUriAsync(CancellationToken.None));
        Assert.Equal(LocalServerHost.FailuresBeforeCooldown + 1, Starts());
    }

    [Fact]
    public async Task IdleAfterLastUse_Stops_ButNotWhileInUse()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using LocalServerHost host = Host("", idleTimeout: TimeSpan.FromMilliseconds(300));

        using (host.BeginUse())
        {
            await host.GetBaseUriAsync(CancellationToken.None);
            await Task.Delay(600, TestContext.Current.CancellationToken);
            Assert.Equal(EEngineRunState.Running, host.State);
        }

        for (int i = 0; i < 50 && host.State != EEngineRunState.Stopped; i++)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(EEngineRunState.Stopped, host.State);
        Assert.Null(host.RunningBaseUri);
    }

    [Fact]
    public async Task LaunchDescriptionFails_PropagatesAsIs_AndIsNotAServerFailure()
    {
        using LocalServerHost host = new(() => throw new InvalidOperationException("no engine"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.GetBaseUriAsync(CancellationToken.None));

        Assert.Null(host.LastError);
        Assert.Equal(EEngineRunState.Stopped, host.State);
    }

    private int Starts() => File.Exists(_counter) ? File.ReadAllLines(_counter).Length : 0;

    // 先跑 prelude（计数、模拟加载耗时或失败），再在分到的端口上起只回 200 的服务
    private LocalServerHost Host(string prelude, TimeSpan? idleTimeout = null)
    {
        string path = Path.Combine(_directory, $"fake-server-{Guid.NewGuid():N}");
        File.WriteAllText(path, $$"""
            #!/bin/sh
            echo x >> '{{_counter}}'
            {{prelude}}
            exec python3 -c "
            import http.server, sys
            class H(http.server.BaseHTTPRequestHandler):
                def do_GET(self):
                    self.send_response(200); self.end_headers(); self.wfile.write(b'{}')
                def log_message(self, *a): pass
            http.server.HTTPServer(('127.0.0.1', int(sys.argv[1])), H).serve_forever()
            " "$1"
            """);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new LocalServerHost(() => new LocalServerLaunch("fake-server", path, port => [port.ToString()],
            TimeSpan.FromSeconds(10)), idleTimeout);
    }
}
