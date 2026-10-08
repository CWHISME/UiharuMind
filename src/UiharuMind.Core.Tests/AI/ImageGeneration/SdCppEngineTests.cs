using System.Runtime.InteropServices;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.Tests.AI.ImageGeneration;

/// <summary>
/// 本地生图引擎：按模型拼启动参数、起 sd-server、提交异步任务、轮询进度与预览、取图；
/// 换模型就换进程，取消在算的就停进程，拒收与失败如实报
/// </summary>
public class SdCppEngineTests : IDisposable
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-sdcpp-{Guid.NewGuid():N}");
    private readonly string _starts;
    private readonly string _argsFile;

    public SdCppEngineTests()
    {
        Directory.CreateDirectory(_directory);
        _starts = Path.Combine(_directory, "starts");
        _argsFile = Path.Combine(_directory, "args");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void Args_PutComponentsFirst_ThenEngineSettings_ThenUserExtras()
    {
        SdCppModelSetup model = Model("z", ("diffusion-model", "/m/z.gguf"), ("vae", "/m/ae.safetensors"), ("llm", "/m/qwen 3.gguf"));
        SdCppSettingConfig settings = new() { OffloadToCpu = true, Threads = 6, ExtraArguments = "--vae-tiling --fa" };

        IReadOnlyList<string> args = SdCppServerArgs.Build(model with { ExtraArguments = ["--vae-format", "flux"] }, settings);

        Assert.Equal(["--diffusion-model", "/m/z.gguf", "--vae", "/m/ae.safetensors", "--llm", "/m/qwen 3.gguf",
            "--vae-format", "flux", "--eager-load", "--diffusion-fa", "--offload-to-cpu", "-t", "6", "--vae-tiling", "--fa"], args);
        Assert.Equal(["--listen-ip", "127.0.0.1", "--listen-port", "4321"],
            SdCppServerArgs.Launch("/bin/sd-server", [], TimeSpan.FromSeconds(1)).BuildArguments(4321));
    }

    [Theory]
    [InlineData("master-945-a1ded76-bin-Darwin-macOS-26.6.2-arm64", "OSX", Architecture.Arm64, true)]
    [InlineData("master-945-a1ded76-bin-win-vulkan-x64", "WINDOWS", Architecture.X64, true)]
    [InlineData("master-945-a1ded76-bin-win-cpu-x64", "WINDOWS", Architecture.X64, false)]
    [InlineData("master-945-a1ded76-bin-Linux-Ubuntu-24.04-x86_64-vulkan", "LINUX", Architecture.X64, true)]
    [InlineData("master-945-a1ded76-bin-Linux-Ubuntu-24.04-x86_64", "LINUX", Architecture.X64, false)]
    public void RecommendedVariant(string name, string platform, Architecture architecture, bool recommended)
    {
        Assert.Equal(recommended, SdCppVariants.IsRecommended(name, OSPlatform.Create(platform), architecture));
    }

    [Fact]
    public void VersionNames_CarryTheBuildNumber_AndPointAtTheirRelease()
    {
        Assert.Equal(new Version(945, 0), ManagedVersionParse("master-945-a1ded76-bin-win-vulkan-x64"));
        Assert.Equal("https://github.com/leejet/stable-diffusion.cpp/releases/tag/master-945-a1ded76",
            SdCppEngine.ReleaseUrl("master-945-a1ded76-bin-win-vulkan-x64"));
    }

    [Fact]
    public async Task Generate_StartsServer_ReportsProgressWithPreview_AndReturnsTheImage()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using SdCppEngine engine = CreateEngine();
        SdCppModelSetup model = Model("z", ("diffusion-model", Touch("z.gguf")));
        List<SdCppProgress> reports = [];

        IReadOnlyList<byte[]> images = await engine.GenerateAsync(model, Request("a cat"),
            new SyncProgress(reports.Add), TestContext.Current.CancellationToken);

        Assert.Equal(Convert.FromBase64String(Png), Assert.Single(images));
        Assert.Contains(reports, x => x is { Stage: ESdCppStage.Generating, Step: 1, TotalSteps: 2, Preview: not null });
        Assert.Contains("--diffusion-model", File.ReadAllText(_argsFile));
        Assert.Equal(EEngineRunState.Running, engine.Runtime.State);
    }

    [Fact]
    public async Task SameModelReusesTheProcess_AnotherModelReplacesIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using SdCppEngine engine = CreateEngine();
        SdCppModelSetup first = Model("a", ("diffusion-model", Touch("a.gguf")));
        SdCppModelSetup second = Model("b", ("diffusion-model", Touch("b.gguf")));
        CancellationToken token = TestContext.Current.CancellationToken;

        await engine.GenerateAsync(first, Request("x"), null, token);
        await engine.GenerateAsync(first, Request("y"), null, token);
        Assert.Equal(1, Starts());

        await engine.GenerateAsync(second, Request("z"), null, token);
        Assert.Equal(2, Starts());
        Assert.Contains("b.gguf", File.ReadAllText(_argsFile));
    }

    [Fact]
    public async Task CancellingWhileGenerating_StopsTheProcess()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using SdCppEngine engine = CreateEngine();
        SdCppModelSetup model = Model("z", ("diffusion-model", Touch("z.gguf")));
        using CancellationTokenSource cancel = new();
        TaskCompletionSource generating = new();

        Task<IReadOnlyList<byte[]>> run = engine.GenerateAsync(model, Request("slow"), new SyncProgress(x =>
        {
            if (x.Stage == ESdCppStage.Generating) generating.TrySetResult();
        }), cancel.Token);
        await generating.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(EEngineRunState.Stopped, engine.Runtime.State);
    }

    [Fact]
    public async Task RejectedAndFailedJobs_AreReportedAsSuch()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using SdCppEngine engine = CreateEngine();
        SdCppModelSetup model = Model("z", ("diffusion-model", Touch("z.gguf")));
        CancellationToken token = TestContext.Current.CancellationToken;

        SdCppRequestException rejected = await Assert.ThrowsAsync<SdCppRequestException>(() =>
            engine.GenerateAsync(model, Request("bad"), null, token));
        Assert.Contains("width", rejected.Message);

        SdCppGenerationException failed = await Assert.ThrowsAsync<SdCppGenerationException>(() =>
            engine.GenerateAsync(model, Request("fail"), null, token));
        Assert.Equal("generation_failed", failed.Code);
    }

    [Fact]
    public async Task MissingComponent_FailsBeforeStartingAnything()
    {
        using SdCppEngine engine = CreateEngine();
        SdCppModelSetup model = Model("z", ("diffusion-model", Path.Combine(_directory, "gone.gguf")));

        SdCppModelMissingException error = await Assert.ThrowsAsync<SdCppModelMissingException>(() =>
            engine.GenerateAsync(model, Request("x"), null, TestContext.Current.CancellationToken));

        Assert.Single(error.Files);
        Assert.Equal(0, Starts());
    }

    private static Version ManagedVersionParse(string name) => UiharuMind.Core.Core.Utils.ManagedVersionPackage.ParseVersion(name);

    private static SdCppImageRequest Request(string prompt) => new(prompt, 512, 512, new SdCppSampling(2, 1f));

    private static SdCppModelSetup Model(string name, params (string Flag, string Path)[] components) =>
        new(name, components.Select(x => new SdCppComponent(x.Flag, x.Path)).ToList(), []);

    private string Touch(string name)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, "");
        return path;
    }

    private int Starts() => File.Exists(_starts) ? File.ReadAllLines(_starts).Length : 0;

    private SdCppEngine CreateEngine()
    {
        string bin = Path.Combine(_directory, "engines", "master-945-a1ded76-bin-Darwin-macOS-26.6.2-arm64");
        Directory.CreateDirectory(bin);
        string exe = Path.Combine(bin, SdCppVersionManager.ExecutableName);
        File.WriteAllText(exe, FakeServerScript());
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        SdCppEngine engine = new(new SdCppSettingConfig(), Path.Combine(_directory, "engines"),
            new DownloadQueue(new FileDownloader(new HttpClient())));
        engine.Distribution.Use(new SdCppVersion
            { Name = Path.GetFileName(bin), InstallDirectory = bin, ExecutablePath = bin, IsInstalled = true });
        return engine;
    }

    // 只认用到的几个接口：prompt 为 bad 时 400、fail 时任务失败、slow 时一直在算，其余第一次查在算、第二次给图
    private string FakeServerScript() => $$$"""
        #!/bin/sh
        echo x >> '{{{_starts}}}'
        echo "$@" > '{{{_argsFile}}}'
        PORT=""
        while [ $# -gt 0 ]; do
          if [ "$1" = "--listen-port" ]; then PORT="$2"; fi
          shift
        done
        exec python3 -c "
        import http.server, json, sys
        jobs = {}
        PNG = '{{{Png}}}'
        class H(http.server.BaseHTTPRequestHandler):
            def send(self, code, body):
                data = json.dumps(body).encode()
                self.send_response(code); self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data)
            def do_GET(self):
                if self.path == '/sdcpp/v1/capabilities': return self.send(200, {'supported_modes': ['img_gen']})
                job = jobs[self.path.rsplit('/', 1)[1]]
                job['polls'] += 1
                if job['prompt'] == 'fail':
                    return self.send(200, {'status': 'failed', 'error': {'code': 'generation_failed', 'message': 'out of memory'}})
                if job['prompt'] == 'slow' or job['polls'] == 1:
                    return self.send(200, {'status': 'generating', 'preview': {'pass': 1, 'step': 1, 'total_steps': 2, 'b64_json': PNG}})
                self.send(200, {'status': 'completed', 'result': {'output_format': 'png', 'images': [{'index': 0, 'b64_json': PNG}]}})
            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                if body['prompt'] == 'bad': return self.send(400, {'error': {'code': 'invalid_request', 'message': 'width must be a multiple of 8'}})
                job_id = 'job_%d' % len(jobs)
                jobs[job_id] = {'prompt': body['prompt'], 'polls': 0}
                self.send(202, {'id': job_id, 'status': 'queued', 'poll_url': '/sdcpp/v1/jobs/' + job_id})
            def log_message(self, *a): pass
        http.server.HTTPServer(('127.0.0.1', int(sys.argv[1])), H).serve_forever()
        " "$PORT"
        """;

    // Progress<T> 会抓同步上下文异步回调，测试里要同步收
    private sealed class SyncProgress(Action<SdCppProgress> report) : IProgress<SdCppProgress>
    {
        public void Report(SdCppProgress value) => report(value);
    }
}
