namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 音频 provider 注册表，与 <see cref="UiharuMind.Core.AI.Runtime.ModelRuntimeBackendRegistry"/> 同构（ADR 0071）。
/// 注册点硬编码在装配处；按能力查（合成 / 听写），preferred 有该能力就优先，否则第一个会的。
/// </summary>
public sealed class AudioProviderRegistry
{
    private readonly List<IAudioProvider> _providers = [];

    public IReadOnlyList<IAudioProvider> Providers => _providers;

    public IReadOnlyList<IAudioProvider> LocalProviders => _providers.Where(x => x.IsLocal).ToList();

    public void Register(IAudioProvider provider)
    {
        if (_providers.Any(x => x.Id == provider.Id))
            throw new InvalidOperationException($"Audio provider '{provider.Id}' is already registered.");
        _providers.Add(provider);
    }

    public IAudioProvider GetRequired(string providerId)
    {
        return _providers.FirstOrDefault(x => x.Id == providerId)
               ?? throw new InvalidOperationException($"Audio provider '{providerId}' is not registered.");
    }

    /// <summary>找会合成的那个：preferred 有 Tts 能力就优先，否则第一个会的</summary>
    public IAudioProvider? FindSynthesizer(string? preferredId = null) => Find(preferredId, EAudioCapability.Tts);

    /// <summary>找会听写的那个：preferred 有 Asr 能力就优先，否则第一个会的</summary>
    public IAudioProvider? FindRecognizer(string? preferredId = null) => Find(preferredId, EAudioCapability.Asr);

    private IAudioProvider? Find(string? preferredId, EAudioCapability capability)
    {
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            IAudioProvider? preferred = _providers.FirstOrDefault(x => x.Id == preferredId);
            if (preferred?.Capabilities.Contains(capability) == true) return preferred;
        }

        return _providers.FirstOrDefault(x => x.Capabilities.Contains(capability));
    }
}
