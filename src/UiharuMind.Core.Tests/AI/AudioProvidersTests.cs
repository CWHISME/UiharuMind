using System.Text.Json;
using EdgeTtsSharp.Structures;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Core.Tests.AI;

/// <summary>在线服务条目、它们的 provider，以及朗读 / 听写各用哪个</summary>
public class AudioProvidersTests
{
    private static readonly AudioServiceInfo Edge = new() { Name = "Edge", Dialect = EAudioDialect.EdgeTts };

    [Fact]
    public async Task EdgeTts_Synthesizes_WithRequestedVoice_AsMp3()
    {
        FakeEdgeGateway gateway = new() { AudioBytes = [1, 2, 3] };
        EdgeTtsProvider provider = new(Edge, gateway);

        AudioSynthesisResult result = await provider.SynthesizeAsync(
            new AudioSynthesisRequest("hi", Voice: "en-US-GuyNeural"), TestContext.Current.CancellationToken);

        Assert.Equal("en-US-GuyNeural", gateway.RequestedVoice);
        Assert.Equal(0, gateway.RequestedRate);
        Assert.Equal(EAudioFormat.Mp3, result.Format);
        using MemoryStream read = new();
        await result.Audio.CopyToAsync(read, TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3], read.ToArray());
    }

    [Theory]
    [InlineData("", "en", "en-US-AriaNeural")] //没设音色：按文字语种挑
    [InlineData("", null, "zh-CN-XiaoxiaoNeural")]
    [InlineData("", "fr", "zh-CN-XiaoxiaoNeural")]
    [InlineData("zh-CN-YunxiNeural", "en", "zh-CN-YunxiNeural")] //条目设了音色就一直用它
    public async Task EdgeTts_Voice_FromEntry_ElseByLanguage(string entryVoice, string? language, string expected)
    {
        FakeEdgeGateway gateway = new();
        AudioServiceInfo service = Edge.Clone();
        service.Voice = entryVoice;

        await new EdgeTtsProvider(service, gateway).SynthesizeAsync(new AudioSynthesisRequest("hi", Language: language),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, gateway.RequestedVoice);
    }

    [Fact]
    public async Task EdgeTts_Speed_MappedToRatePercent_UnknownVoiceFails()
    {
        FakeEdgeGateway gateway = new();
        EdgeTtsProvider provider = new(Edge, gateway);

        await provider.SynthesizeAsync(new AudioSynthesisRequest("hi", Speed: 1.5f), TestContext.Current.CancellationToken);
        Assert.Equal(50, gateway.RequestedRate);

        gateway.Known = false;
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.SynthesizeAsync(new AudioSynthesisRequest("hi"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EdgeTts_StalledStream_TimesOut_AndCancelReturnsAtOnce()
    {
        FakeEdgeGateway gateway = new() { Stall = true };
        EdgeTtsProvider provider = new(Edge, gateway, TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            provider.SynthesizeAsync(new AudioSynthesisRequest("hi"), TestContext.Current.CancellationToken));

        provider = new EdgeTtsProvider(Edge, gateway, TimeSpan.FromMinutes(5));
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(100));
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.SynthesizeAsync(new AudioSynthesisRequest("hi"), cancel.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), "卡住的流读不认取消，也要能立刻停");
    }

    [Fact]
    public void AudioServiceInfo_ApiKey_RoundTripsEncrypted_AndCloneKeepsIt()
    {
        AudioServiceInfo service = new() { Name = "svc", Endpoint = "http://x/v1", ModelId = "m", Task = EAudioCapability.Asr };
        service.ApiKey = "sk-secret-123";

        string json = JsonSerializer.Serialize(service);
        Assert.DoesNotContain("sk-secret-123", json); // 盘上是密文
        Assert.Contains("\"ApiKey\":\"", json);

        AudioServiceInfo restored = JsonSerializer.Deserialize<AudioServiceInfo>(json)!;
        Assert.Equal("sk-secret-123", restored.ApiKey);
        Assert.Equal(EAudioCapability.Asr, restored.Task);
        Assert.True(restored.IsConfigured);
        Assert.Equal("sk-secret-123", restored.Clone().ApiKey);
    }

    [Fact]
    public void AudioServiceInfo_Configured_EdgeAlways_CompatibleNeedsEndpointAndModel()
    {
        Assert.True(Edge.IsConfigured);
        Assert.False(new AudioServiceInfo { ModelId = "m" }.IsConfigured);
        Assert.False(new AudioServiceInfo { Endpoint = "http://x" }.IsConfigured);
    }

    [Fact]
    public async Task Factory_MapsDialects_CapabilityFollowsTask()
    {
        Assert.IsType<EdgeTtsProvider>(AudioServiceProviderFactory.Create(Edge));

        using OpenAiAudioServiceProvider asr = Assert.IsType<OpenAiAudioServiceProvider>(AudioServiceProviderFactory.Create(
            new AudioServiceInfo { Name = "svc", Endpoint = "http://x/v1", ModelId = "m", Task = EAudioCapability.Asr }));
        Assert.Equal("service:svc", asr.Id);
        Assert.Equal(EAudioProviderKind.OnlineService, asr.Kind);
        Assert.Equal([EAudioCapability.Asr], asr.Capabilities);
        Assert.False(asr.IsLocal);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            asr.SynthesizeAsync(new AudioSynthesisRequest("hi"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Preset_Match_FindsByDialectTaskAndEndpoint()
    {
        Assert.Equal("edge", AudioServicePreset.Match(Edge).Key);
        Assert.Equal("siliconflow-asr", AudioServicePreset.Match(new AudioServiceInfo
            { Task = EAudioCapability.Asr, Endpoint = "https://api.siliconflow.cn/v1/" }).Key);
        Assert.Equal(AudioServicePreset.CustomKey, AudioServicePreset.Match(new AudioServiceInfo
            { Endpoint = "http://127.0.0.1:8000/v1" }).Key);
    }

    [Fact]
    public void Router_DefaultIsLocal_ServiceWhenChosen_BackToLocalWhenItGoes()
    {
        AudioServiceSettingConfig services = new();
        AudioSettingConfig settings = new();
        FakeProvider local = new("AudioCpp");
        List<AudioServiceInfo> created = [];
        using AudioRouter router = new(() => local, settings, services, s =>
        {
            created.Add(s);
            return new FakeProvider(s.Id);
        });
        AudioServiceInfo edge = Edge.Clone();
        AudioServiceInfo asr = new() { Name = "SenseVoice", Task = EAudioCapability.Asr, Endpoint = "http://x", ModelId = "m" };
        services.ReplaceServices([edge, asr]);
        int changes = 0;
        router.DefaultsChanged += () => changes++;

        Assert.Same(local, router.Resolve(EAudioCapability.Tts));
        Assert.Equal("service:Edge", router.Resolve(EAudioCapability.Tts, "service:Edge").Id); //试听指定哪个就用哪个
        Assert.Same(local, router.Resolve(EAudioCapability.Asr, "service:Edge")); //任务对不上不用

        router.UseService(edge);
        router.UseService(asr);
        Assert.Same(edge, router.DefaultService(EAudioCapability.Tts));
        Assert.Equal("service:SenseVoice", router.Resolve(EAudioCapability.Asr).Id);
        router.Resolve(EAudioCapability.Tts);
        Assert.Equal(2, created.Count); //同一条目只建一次 provider
        Assert.Equal(2, changes);

        AudioServiceInfo renamed = edge.Clone();
        renamed.Name = "微软";
        services.ReplaceServices([renamed, asr]);
        router.OnServiceRenamed("Edge", renamed);
        Assert.Equal("service:微软", settings.PreferredTtsProviderId);

        services.ReplaceServices([asr]);
        Assert.Same(local, router.Resolve(EAudioCapability.Tts)); //服务删了回到本地
        router.UseLocal(EAudioCapability.Asr);
        Assert.Same(local, router.Resolve(EAudioCapability.Asr));
    }

    [Fact]
    public void Router_NoLocalModelForTask_FallsBackToFirstService()
    {
        AudioServiceSettingConfig services = new();
        FakeProvider local = new("AudioCpp");
        bool hasLocalTts = false;
        using AudioRouter router = new(() => local, new AudioSettingConfig(), services, s => new FakeProvider(s.Id),
            (task, _) => task == EAudioCapability.Tts ? hasLocalTts : true);

        Assert.Same(local, router.Resolve(EAudioCapability.Tts)); //一个在线服务都没有：还是本地，由它报缺模型
        services.ReplaceServices([Edge.Clone()]);
        Assert.Equal("service:Edge", router.Resolve(EAudioCapability.Tts).Id);
        Assert.Equal("Edge", router.DefaultService(EAudioCapability.Tts)?.Name);

        hasLocalTts = true;
        Assert.Same(local, router.Resolve(EAudioCapability.Tts));
    }

    [Fact]
    public void Router_FallbackForAFailedService_OnlyWhenTurnedOn_AndLocalSpeaksTheLanguage()
    {
        FakeProvider local = new("AudioCpp") { IsLocal = true };
        FakeProvider online = new("service:Edge");
        AudioSettingConfig settings = new();
        bool hasLocalTts = true;
        using AudioRouter router = new(() => local, settings, new AudioServiceSettingConfig(),
            hasLocal: (task, language) => task == EAudioCapability.Tts && hasLocalTts && language != "ja");

        Assert.Null(router.Fallback(EAudioCapability.Tts, online)); //默认关
        settings.FallbackToLocal = true;
        Assert.Same(local, router.Fallback(EAudioCapability.Tts, online, "zh"));
        Assert.Null(router.Fallback(EAudioCapability.Tts, online, "ja")); //本地没有会日语的
        Assert.Null(router.Fallback(EAudioCapability.Tts, local)); //本地自己失败的不再换
        Assert.Null(router.Fallback(EAudioCapability.Asr, online));
        hasLocalTts = false;
        Assert.Null(router.Fallback(EAudioCapability.Tts, online));
    }

    [Fact]
    public void OnlyNetworkFailures_FallBack()
    {
        using CancellationTokenSource stopped = new();
        stopped.Cancel();

        Assert.True(FallbackAudioProvider.IsNetworkFailure(new HttpRequestException("no route"), CancellationToken.None));
        Assert.True(FallbackAudioProvider.IsNetworkFailure(new TaskCanceledException("timeout", new TimeoutException()),
            CancellationToken.None));
        Assert.True(FallbackAudioProvider.IsNetworkFailure(new TimeoutException(), CancellationToken.None));
        Assert.True(FallbackAudioProvider.IsNetworkFailure(
            new AudioServiceException("down", System.Net.HttpStatusCode.BadGateway, null), CancellationToken.None));
        Assert.False(FallbackAudioProvider.IsNetworkFailure(
            new AudioServiceException("bad key", System.Net.HttpStatusCode.Unauthorized, null), CancellationToken.None));
        Assert.False(FallbackAudioProvider.IsNetworkFailure(
            new AudioServiceException("slow down", System.Net.HttpStatusCode.TooManyRequests, null), CancellationToken.None));
        Assert.False(FallbackAudioProvider.IsNetworkFailure(new NotSupportedException("no such voice"), CancellationToken.None));
        Assert.False(FallbackAudioProvider.IsNetworkFailure(new OperationCanceledException(stopped.Token), stopped.Token));
    }

    private sealed class FakeEdgeGateway : IEdgeTtsGateway
    {
        public bool Known { get; set; } = true;
        public bool Stall { get; set; }
        public byte[] AudioBytes { get; set; } = [1];
        public string? RequestedVoice { get; private set; }
        public int RequestedRate { get; private set; }

        public Task<Voice?> GetVoiceAsync(string shortName, CancellationToken cancellationToken)
        {
            RequestedVoice = shortName;
            return Task.FromResult(Known ? new Voice { ShortName = shortName } : null);
        }

        public Task<IReadOnlyList<string>> GetVoiceNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["zh-CN-XiaoxiaoNeural"]);

        public Stream GetAudioStream(Voice voice, string text, int rate, CancellationToken cancellationToken)
        {
            RequestedRate = rate;
            return Stall ? new StalledStream() : new MemoryStream(AudioBytes);
        }
    }

    // 照连接半路卡住的样子：读永远不返回，也不认取消
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(new TaskCompletionSource<int>().Task);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeProvider(string id) : IAudioProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public EAudioProviderKind Kind => EAudioProviderKind.OnlineService;
        public IReadOnlySet<EAudioCapability> Capabilities { get; } = new HashSet<EAudioCapability>();
        public bool IsLocal { get; init; }

        public Task<AudioSynthesisResult> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
