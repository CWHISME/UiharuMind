using System.Runtime.InteropServices;
using System.Text.Json;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// audio.cpp 引擎：变体挑选、server.json 生成、进程宿主。
/// 假 server 用 python3 起一个只回 /health 200 的迷你 HTTP 服务（macOS 自带 python3）
/// </summary>
public class AudioCppEngineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-audiocpp-{Guid.NewGuid():N}");

    public AudioCppEngineTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData("audio-v0.9.1-bin-macos-arm64-metal", "https://github.com/0xShug0/audio.cpp/releases/tag/v0.9.1")]
    [InlineData("custom-build", null)]
    public void ReleaseUrl_FromReleaseTag(string name, string? expected)
    {
        Assert.Equal(expected, new AudioCppVersionManager().ReleaseUrl(name));
    }

    [Theory]
    [InlineData("audio-v0.9.1-bin-macos-arm64-metal", "OSX", Architecture.Arm64, true)]
    [InlineData("audio-v0.9.1-bin-macos-x64-metal", "OSX", Architecture.Arm64, false)]
    [InlineData("audio-v0.9.1-bin-windows-x64-cpu", "WINDOWS", Architecture.X64, true)]
    [InlineData("audio-v0.9.1-bin-windows-x64-cpu-portable", "WINDOWS", Architecture.X64, false)]
    [InlineData("audio-v0.9.1-bin-windows-x64-vulkan", "WINDOWS", Architecture.X64, false)]
    [InlineData("audio-v0.9.1-bin-ubuntu-x64-cpu", "LINUX", Architecture.X64, true)]
    [InlineData("audio-v0.9.1-bin-ubuntu-x64-cpu", "LINUX", Architecture.Arm64, false)]
    public void RecommendedVariant_PerPlatform(string package, string platform, Architecture architecture, bool expected)
    {
        Assert.Equal(expected, AudioCppVariants.IsRecommended(package, OSPlatform.Create(platform), architecture));
    }

    [Theory]
    [InlineData("audio-v0.9.1-bin-macos-arm64-metal", "metal")]
    [InlineData("audio-v0.9.1-bin-windows-x64-vulkan", "vulkan")]
    [InlineData("audio-v0.9.1-bin-ubuntu-x64-cpu-portable", "cpu")]
    public void Backend_FollowsVariant(string package, string backend)
    {
        Assert.Equal(backend, AudioCppVariants.GetBackend(package));
    }

    [Theory]
    [InlineData(false, "metal")]
    [InlineData(true, "cpu")] //Metal 跑不动某个模型时的退路
    public void ServerOptions_BackendFollowsVariant_UnlessForcedToCpu(bool forceCpu, string backend)
    {
        AudioCppSettingConfig settings = new() { ForceCpu = forceCpu, MinFreeMemoryMb = -5, IdleUnloadMinutes = 2 };

        AudioCppServerOptions options = AudioCppEngine.BuildServerOptions("audio-v0.9.1-bin-macos-arm64-metal", settings);

        Assert.Equal(backend, options.Backend);
        Assert.Equal(0, options.MinFreeMemoryMb); //负数按不检查
        Assert.Equal(120_000, options.IdleUnloadMs);
    }

    [Fact]
    public void WriteConfig_ContainsPortModelsAndVoicePreset()
    {
        string path = Path.Combine(_directory, "server.json");
        AudioCppServerConfig.Write(path, 18099,
            [TtsModel(), new AudioCppModelEntry("asr", "moonshine_asr", "/m/m.gguf", "asr")],
            new AudioCppServerOptions("metal", Threads: 2, IdleUnloadMs: 60000, MinFreeMemoryMb: 1024, VoiceDir: "/voices"));

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        Assert.Equal(18099, root.GetProperty("port").GetInt32());
        Assert.Equal("metal", root.GetProperty("backend").GetString());
        Assert.Equal(60000, root.GetProperty("idle_unload_ms").GetInt32());
        Assert.Equal(1024, root.GetProperty("min_free_memory_mb").GetInt32());
        Assert.Equal("/voices", root.GetProperty("voice_dir").GetString());
        Assert.True(root.GetProperty("lazy_load").GetBoolean());

        JsonElement models = root.GetProperty("models");
        Assert.Equal(2, models.GetArrayLength());
        JsonElement tts = models[0];
        Assert.Equal("pocket-tts", tts.GetProperty("id").GetString());
        Assert.Equal("pocket_tts", tts.GetProperty("family").GetString());
        Assert.Equal("/models/pocket.gguf", tts.GetProperty("path").GetString());
        Assert.Equal("tts", tts.GetProperty("task").GetString());
        Assert.Equal("alba", tts.GetProperty("default_voice_preset").GetProperty("voice_id").GetString());
        Assert.Equal("english", tts.GetProperty("load_options").GetProperty("language").GetString());
    }

    [Fact]
    public async Task Launch_WritesConfigWithAssignedPort_AndBecomesHealthy()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        string exe = WriteScript("");
        string configPath = Path.Combine(_directory, "server.json");

        using CancellationTokenSource cts = new();
        LocalServerProcess server = await LocalServerProcess.StartAsync(
            AudioCppServerConfig.Launch(exe, configPath, [TtsModel()], new AudioCppServerOptions("cpu"),
                TimeSpan.FromSeconds(10)), null, cts.Token);

        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath)))
            Assert.Equal(server.Port, doc.RootElement.GetProperty("port").GetInt32());

        cts.Cancel();
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static AudioCppModelEntry TtsModel() => new(
        "pocket-tts", "pocket_tts", "/models/pocket.gguf", "tts",
        LoadOptions: new Dictionary<string, string> { ["language"] = "english" },
        DefaultVoice: "alba");

    // 先跑 prelude（计数、模拟加载耗时），再按 server.json 里的端口起只回 200 的服务
    private string WriteScript(string prelude)
    {
        string path = Path.Combine(_directory, $"fake-audiocpp-server-{Guid.NewGuid():N}");
        File.WriteAllText(path, $$"""
            #!/bin/sh
            {{prelude}}
            PORT=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['port'])" "$2")
            exec python3 -c "
            import http.server, sys
            class H(http.server.BaseHTTPRequestHandler):
                def do_GET(self):
                    self.send_response(200); self.end_headers(); self.wfile.write(b'{}')
                def log_message(self, *a): pass
            http.server.HTTPServer(('127.0.0.1', int(sys.argv[1])), H).serve_forever()
            " "$PORT"
            """);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
