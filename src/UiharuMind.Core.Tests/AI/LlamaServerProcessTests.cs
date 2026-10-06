using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// llama-server 起不来时必须如实失败、带上日志尾巴。
/// 旧实现看到 "error" 开头的日志就把失败的进程标成已加载，用户只看到一个永远不回话的模型。
/// </summary>
public class LlamaServerProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-server-{Guid.NewGuid():N}");

    public LlamaServerProcessTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task ProcessExitingBeforeReady_FailsWithExitCodeAndLogTail()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        string exe = WriteScript("echo 'load_tensors: loading'\necho 'error: failed to load model' >&2\nexit 3");

        LlamaServerException error = await Assert.ThrowsAsync<LlamaServerException>(() =>
            LlamaServerProcess.StartAsync(exe, [], TimeSpan.FromSeconds(10), null, CancellationToken.None));

        Assert.Contains("exit code 3", error.Message);
        Assert.Contains("failed to load model", error.LogTail);
        Assert.Contains("failed to load model", error.Detail);
    }

    [Fact]
    public async Task NeverHealthy_TimesOutAndStopsTheProcess()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        string exe = WriteScript("echo 'still loading'\nsleep 30");

        LlamaServerException error = await Assert.ThrowsAsync<LlamaServerException>(() =>
            LlamaServerProcess.StartAsync(exe, [], TimeSpan.FromMilliseconds(600), null, CancellationToken.None));

        Assert.Contains("not ready", error.Message);
    }

    [Fact]
    public async Task MissingExecutable_Fails()
    {
        await Assert.ThrowsAsync<LlamaServerException>(() =>
            LlamaServerProcess.StartAsync(Path.Combine(_directory, "nope"), [], TimeSpan.FromSeconds(1), null,
                CancellationToken.None));
    }

    [Fact]
    public void FreePort_IsUsable()
    {
        Assert.InRange(LlamaServerProcess.FindFreePort(), 1, 65535);
    }

    [Fact]
    public void ChatArgs_KeepPathsWithSpacesWhole_AndCarryProjector()
    {
        GGufModelInfo model = new()
        {
            ModelName = "My Model",
            ModelPath = "/models/My Model.gguf",
            ModelProjPath = "/models/mmproj F16.gguf"
        };
        RuntimeResolvedParameters parameters = new(8192, 512, 256, 99, 0, true, false, "");

        List<string> args = [..LLamaCppServerArgs.Build(model, parameters, new LLamaCppServerOptions())];

        Assert.Equal("My Model", args[args.IndexOf("--alias") + 1]);
        Assert.Equal("/models/mmproj F16.gguf", args[args.IndexOf("--mmproj") + 1]);
        Assert.Equal("on", args[args.IndexOf("--flash-attn") + 1]);
        Assert.Contains("--jinja", args);
        Assert.DoesNotContain("--port", args); //端口由进程自选
    }

    private string WriteScript(string body)
    {
        string path = Path.Combine(_directory, "fake-llama-server");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
