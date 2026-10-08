using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Runtime;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 本地引擎 provider 端到端：假 audiocpp_server（python 起一个迷你服务）走完设置 → server.json → 进程 → 请求。
/// 假服务和 pocket_tts 一样：合成不给音色就 500
/// </summary>
public class AudioCppProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-audiocpp-provider-{Guid.NewGuid():N}");
    private readonly AudioCppSettingConfig _settings = new();
    private readonly List<AudioCppModelEntry> _models =
    [
        new("tts-a", "pocket_tts", "/models/a.gguf", "tts"),
        new("asr-a", "moonshine_asr", "/models/m.gguf", "asr")
    ];

    public AudioCppProviderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task Synthesize_WithoutVoice_PicksFirstListedVoice_ThenTranscribes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using AudioCppEngine engine = CreateEngine(installed: true);
        using AudioCppProvider provider = new(engine);

        AudioSynthesisResult tts = await provider.SynthesizeAsync(new AudioSynthesisRequest("hello"), CancellationToken.None);
        Assert.Equal(EAudioFormat.Wav, tts.Format);
        Assert.Equal("tts-a:alba", await ReadText(tts.Audio));

        AudioTranscriptionResult asr = await provider.TranscribeAsync(
            new AudioTranscriptionRequest(new MemoryStream([1, 2, 3]), EAudioFormat.Wav), CancellationToken.None);
        Assert.Equal("asr-a heard you", asr.Text);
    }

    [Fact]
    public async Task Synthesize_UsesVoiceChosenForThatModel()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        _settings.TtsVoices["tts-a"] = "marius";
        using AudioCppEngine engine = CreateEngine(installed: true);
        using AudioCppProvider provider = new(engine);

        AudioSynthesisResult tts = await provider.SynthesizeAsync(new AudioSynthesisRequest("hello"), CancellationToken.None);

        Assert.Equal("tts-a:marius", await ReadText(tts.Audio));
    }

    [Fact]
    public async Task SelectingModelTheServerDoesNotKnow_RestartsWithIt()
    {
        // 刚下完的模型不在已起的 server.json 里，不重启就会被 server 拒掉
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using AudioCppEngine engine = CreateEngine(installed: true);
        using AudioCppProvider provider = new(engine);
        await provider.SynthesizeAsync(new AudioSynthesisRequest("one"), CancellationToken.None);

        _models.Add(new AudioCppModelEntry("tts-b", "kokoro_tts", "/models/b.gguf", "tts"));
        _settings.TtsModel = "tts-b";
        AudioSynthesisResult tts = await provider.SynthesizeAsync(new AudioSynthesisRequest("two"), CancellationToken.None);

        Assert.Equal("tts-b:alba", await ReadText(tts.Audio));
        Assert.Equal(2, File.ReadAllLines(Path.Combine(_directory, "starts")).Length);
    }

    [Fact]
    public async Task NoEngineInstalled_FailsAsNotReady()
    {
        using AudioCppEngine engine = CreateEngine(installed: false);
        using AudioCppProvider provider = new(engine);

        await Assert.ThrowsAsync<LocalEngineNotReadyException>(() =>
            provider.SynthesizeAsync(new AudioSynthesisRequest("hi"), CancellationToken.None));
    }

    [Fact]
    public async Task NoModelForTask_FailsAsModelMissing()
    {
        _models.RemoveAll(x => x.Task == "asr");
        using AudioCppEngine engine = CreateEngine(installed: false);
        using AudioCppProvider provider = new(engine);

        AudioModelMissingException error = await Assert.ThrowsAsync<AudioModelMissingException>(() =>
            provider.TranscribeAsync(new AudioTranscriptionRequest(new MemoryStream(), EAudioFormat.Wav),
                CancellationToken.None));
        Assert.Equal(EAudioCppTask.Asr, error.Task);
    }

    [Fact]
    public void ServerPath_IsPackageDirectory_OnlyWhenItHoldsOneGguf()
    {
        string single = Path.Combine(_directory, "single");
        string shared = Path.Combine(_directory, "shared");
        Directory.CreateDirectory(single);
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(single, "a.gguf"), "");
        File.WriteAllText(Path.Combine(shared, "a-q8_0.gguf"), "");
        File.WriteAllText(Path.Combine(shared, "a-bf16.gguf"), "");

        Assert.Equal(single, AudioCppModels.ServerPath(Path.Combine(single, "a.gguf")));
        Assert.Equal(Path.Combine(shared, "a-bf16.gguf"), AudioCppModels.ServerPath(Path.Combine(shared, "a-bf16.gguf")));
    }

    private AudioCppEngine CreateEngine(bool installed)
    {
        AudioCppEngine engine = new(_settings, Path.Combine(_directory, "engines"),
            Path.Combine(_directory, "server.json"), () => _models.ToList());
        if (installed)
        {
            string bin = Path.Combine(_directory, "bin");
            Directory.CreateDirectory(bin);
            WriteFakeServer(Path.Combine(bin, AudioCppVersionManager.ServerExeName));
            engine.UseVersion(new AudioCppVersion { Name = "audio-v0.9.1-bin-macos-arm64-metal", ExecutablePath = bin });
        }

        return engine;
    }

    private static async Task<string> ReadText(Stream stream)
    {
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }

    // 合成回「模型:音色」，听写回「模型 heard you」，每次启动在 starts 里记一行
    private void WriteFakeServer(string path)
    {
        string counter = Path.Combine(_directory, "starts");
        File.WriteAllText(path, $$"""
            #!/bin/sh
            echo x >> '{{counter}}'
            exec python3 -c "
            import http.server, json, sys, re
            port = json.load(open(sys.argv[1]))['port']
            class H(http.server.BaseHTTPRequestHandler):
                def reply(self, code, body, kind='application/json'):
                    self.send_response(code); self.send_header('Content-Type', kind); self.end_headers(); self.wfile.write(body)
                def do_GET(self):
                    if self.path == '/health': self.reply(200, b'{}')
                    elif self.path.startswith('/v1/audio/voices'): self.reply(200, b'{\"voices\":[\"alba\",\"marius\"]}')
                    else: self.reply(404, b'{}')
                def do_POST(self):
                    body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
                    if self.path == '/v1/audio/speech':
                        req = json.loads(body)
                        if not req.get('voice'): self.reply(500, b'{\"error\":\"requires a session voice\"}'); return
                        self.reply(200, (req['model'] + ':' + req['voice']).encode(), 'audio/wav')
                    elif self.path == '/v1/audio/transcriptions':
                        model = re.search(rb'name=\"?model\"?\r\n(?:[^\r]+\r\n)*\r\n([^\r]+)', body).group(1).decode()
                        self.reply(200, json.dumps({'text': model + ' heard you'}).encode())
                    else: self.reply(404, b'{}')
                def log_message(self, *a): pass
            http.server.HTTPServer(('127.0.0.1', port), H).serve_forever()
            " "$2"
            """);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
