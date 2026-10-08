using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 音频 provider 可插拔：TTS / ASR 各按能力查，preferred 有该能力就优先，
/// 旧档案里存的未注册 Id 必须落到还能跑的那个（与 ModelRuntimeBackendRegistry 同语义）。
/// </summary>
public class AudioProviderRegistryTests
{
    private readonly AudioProviderRegistry _registry = new();

    public AudioProviderRegistryTests()
    {
        _registry.Register(new FakeProvider("RemoteTts", EAudioProviderKind.OnlineService, isLocal: false, EAudioCapability.Tts));
        _registry.Register(new FakeProvider("LocalAll", EAudioProviderKind.LocalEngine, isLocal: true, EAudioCapability.Tts, EAudioCapability.Asr));
        _registry.Register(new FakeProvider("System", EAudioProviderKind.SystemBuiltIn, isLocal: true, EAudioCapability.Tts));
        _registry.Register(new FakeProvider("AsrOnly", EAudioProviderKind.LocalEngine, isLocal: true, EAudioCapability.Asr));
    }

    [Fact]
    public void Preferred_WinsWhenItHasTheCapability()
    {
        Assert.Equal("LocalAll", _registry.FindSynthesizer("LocalAll")?.Id);
        Assert.Equal("AsrOnly", _registry.FindRecognizer("AsrOnly")?.Id);
    }

    [Fact]
    public void Preferred_WithoutCapability_FallsThrough()
    {
        // AsrOnly 没有 Tts：找合成时要绕过它
        Assert.NotEqual("AsrOnly", _registry.FindSynthesizer("AsrOnly")?.Id);
        // RemoteTts 没有 Asr：找听写时要绕过它
        Assert.NotEqual("RemoteTts", _registry.FindRecognizer("RemoteTts")?.Id);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownOrUnsetPreferred_FallsBackToFirstCapable(string? preferred)
    {
        Assert.Equal("RemoteTts", _registry.FindSynthesizer(preferred)?.Id);
        Assert.Equal("LocalAll", _registry.FindRecognizer(preferred)?.Id);
    }

    [Fact]
    public void LocalProviders_ExcludeOnline()
    {
        Assert.Equal(["LocalAll", "System", "AsrOnly"], _registry.LocalProviders.Select(x => x.Id));
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _registry.Register(new FakeProvider("LocalAll", EAudioProviderKind.LocalEngine, true, EAudioCapability.Tts)));
    }

    [Fact]
    public void GetRequired_UnknownId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _registry.GetRequired("Nope"));
        Assert.Equal("LocalAll", _registry.GetRequired("LocalAll").Id);
    }

    private sealed class FakeProvider(string id, EAudioProviderKind kind, bool isLocal, params EAudioCapability[] capabilities)
        : IAudioProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public EAudioProviderKind Kind => kind;
        public IReadOnlySet<EAudioCapability> Capabilities { get; } = new HashSet<EAudioCapability>(capabilities);
        public bool IsLocal => isLocal;

        public Task<AudioSynthesisResult> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
