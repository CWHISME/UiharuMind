using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.Core.DevControl;

namespace UiharuMind.Core.Tests;

/// <summary>
/// 开发控制通道（ADR 0059）：真 socket，各用各的临时档案目录。
/// 目录名要短：macOS 的 Unix socket 路径上限 104 字节
/// </summary>
public sealed class DevControlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"udc{Guid.NewGuid():N}"[..12]);
    private readonly DevControlEndpoint _endpoint;

    public DevControlTests()
    {
        _endpoint = new DevControlEndpoint(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Task<DevStepOutcome> Echo(string op, JsonElement args) =>
        Task.FromResult(new DevStepOutcome(true, 1, new { op, args = args.ValueKind == JsonValueKind.Undefined ? null : args.GetRawText() }));

    [Fact]
    public async Task Call_RoundTripsThroughTheHandler()
    {
        using DevControlServer server = new(_endpoint, Echo);
        Assert.True(server.Start());

        await using DevControlClient client = await DevControlClient.ConnectAsync(_endpoint, TestContext.Current.CancellationToken);
        JsonObject ping = await client.CallAsync("app.ping", token: TestContext.Current.CancellationToken);
        JsonObject reply = await client.CallAsync("group.post", new JsonObject { ["text"] = "hi" }, TestContext.Current.CancellationToken);

        Assert.True(ping["ok"]!.GetValue<bool>());
        Assert.Equal(Environment.ProcessId, ping["result"]!["pid"]!.GetValue<int>());
        Assert.True(reply["ok"]!.GetValue<bool>());
        Assert.Equal("group.post", reply["result"]!["op"]!.GetValue<string>());
        Assert.Equal("{\"text\":\"hi\"}", reply["result"]!["args"]!.GetValue<string>());
    }

    /// <summary>一步挂着等的时候，同一条连接上后发的那步先回来</summary>
    [Fact]
    public async Task Calls_DoNotQueueBehindAWaitingStep()
    {
        TaskCompletionSource<DevStepOutcome> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using DevControlServer server = new(_endpoint, (op, _) =>
            op == "group.wait" ? waiting.Task : Task.FromResult(new DevStepOutcome(true, 0, op)));
        server.Start();
        await using DevControlClient waiter = await DevControlClient.ConnectAsync(_endpoint, TestContext.Current.CancellationToken);
        await using DevControlClient other = await DevControlClient.ConnectAsync(_endpoint, TestContext.Current.CancellationToken);

        Task<JsonObject> wait = waiter.CallAsync("group.wait", token: TestContext.Current.CancellationToken);
        JsonObject post = await other.CallAsync("group.post", token: TestContext.Current.CancellationToken);

        Assert.Equal("group.post", post["result"]!.GetValue<string>());
        Assert.False(wait.IsCompleted);
        waiting.SetResult(new DevStepOutcome(true, 0, "done"));
        Assert.Equal("done", (await wait)["result"]!.GetValue<string>());
    }

    [Fact]
    public async Task HandlerFailure_ComesBackAsError()
    {
        using DevControlServer server = new(_endpoint, (_, _) => throw new ArgumentException("missing arg 'group'"));
        server.Start();

        await using DevControlClient client = await DevControlClient.ConnectAsync(_endpoint, TestContext.Current.CancellationToken);
        JsonObject reply = await client.CallAsync("group.post", token: TestContext.Current.CancellationToken);

        Assert.False(reply["ok"]!.GetValue<bool>());
        Assert.Equal("ArgumentException: missing arg 'group'", reply["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task WrongToken_IsRejected()
    {
        using DevControlServer server = new(_endpoint, Echo);
        server.Start();
        await File.WriteAllTextAsync(_endpoint.TokenPath, "forged", TestContext.Current.CancellationToken);
        await using DevControlClient forged = await DevControlClient.ConnectAsync(_endpoint, TestContext.Current.CancellationToken);

        JsonObject reply = await forged.CallAsync("group.post", token: TestContext.Current.CancellationToken);

        Assert.False(reply["ok"]!.GetValue<bool>());
        Assert.Equal("bad token", reply["error"]!.GetValue<string>());
    }

    [Fact]
    public void TokenAndDirectory_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) return;
        using DevControlServer server = new(_endpoint, Echo);
        server.Start();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_endpoint.RunDirectory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_endpoint.TokenPath));
    }

    [Fact]
    public void SecondInstance_DoesNotTakeOver()
    {
        using DevControlServer first = new(_endpoint, Echo);
        Assert.True(first.Start());
        string token = File.ReadAllText(_endpoint.TokenPath);

        using (DevControlServer second = new(_endpoint, Echo))
        {
            Assert.False(second.Start());
        }

        Assert.Equal(token, File.ReadAllText(_endpoint.TokenPath)); //后起的没动先起的令牌，收尾也没删
        Assert.True(File.Exists(_endpoint.SocketPath));
    }

    [Fact]
    public async Task LeftoverSocket_IsReplaced_AndStopCleansUp()
    {
        using (DevControlServer crashed = new(_endpoint, Echo))
        {
            crashed.Start();
        }

        Assert.False(File.Exists(_endpoint.SocketPath));
        Assert.False(File.Exists(_endpoint.TokenPath));

        Directory.CreateDirectory(_endpoint.RunDirectory);
        await File.WriteAllTextAsync(_endpoint.SocketPath, "", TestContext.Current.CancellationToken); //没人监听的残留
        using DevControlServer server = new(_endpoint, Echo);
        Assert.True(server.Start());

        await using DevControlClient client = await DevControlClient.ConnectAsync(_endpoint, TestContext.Current.CancellationToken);
        Assert.True((await client.CallAsync("app.ping", token: TestContext.Current.CancellationToken))["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task MalformedRequest_IsAnsweredThenDisconnected()
    {
        using DevControlServer server = new(_endpoint, Echo);
        server.Start();
        using Socket raw = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await raw.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint.SocketPath), TestContext.Current.CancellationToken);
        await using NetworkStream stream = new(raw);
        using StreamReader reader = new(stream);

        await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"id\":1}\n"), TestContext.Current.CancellationToken);

        Assert.Contains("malformed request", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Null(await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>结果序列化不了也要回一句，客户端不能干等</summary>
    [Fact]
    public async Task UnserializableResult_ComesBackAsError()
    {
        using DevControlServer server = new(_endpoint, (_, _) => Task.FromResult(new DevStepOutcome(true, 0, new Cyclic())));
        server.Start();

        await using DevControlClient client = await DevControlClient.ConnectAsync(_endpoint, TestContext.Current.CancellationToken);
        JsonObject reply = await client.CallAsync("x", token: TestContext.Current.CancellationToken);

        Assert.False(reply["ok"]!.GetValue<bool>());
        Assert.StartsWith("result not serializable", reply["error"]!.GetValue<string>());
    }

    [Fact]
    public void SymlinkedRunDirectory_IsRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        string elsewhere = _root + "x";
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(_root);
        Directory.CreateSymbolicLink(_endpoint.RunDirectory, elsewhere);
        try
        {
            using DevControlServer server = new(_endpoint, Echo);
            Assert.Throws<IOException>(() => server.Start());
            Assert.Empty(Directory.GetFiles(elsewhere));
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>绑不上（macOS socket 路径上限 104 字节）就不留下令牌</summary>
    [Fact]
    public void BindFailure_LeavesNoToken()
    {
        if (OperatingSystem.IsWindows()) return;
        DevControlEndpoint tooLong = new(Path.Combine(_root, new string('d', 120)));
        using DevControlServer server = new(tooLong, Echo);

        Assert.ThrowsAny<Exception>(() => server.Start());
        Assert.False(File.Exists(tooLong.TokenPath));
    }

    [Fact]
    public async Task Connect_WhenClosed_SaysSo()
    {
        InvalidOperationException e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevControlClient.ConnectAsync(_endpoint, TestContext.Current.CancellationToken));

        Assert.Contains("not open", e.Message);
    }

    private sealed class Cyclic
    {
        public Cyclic Self => this;
    }
}
