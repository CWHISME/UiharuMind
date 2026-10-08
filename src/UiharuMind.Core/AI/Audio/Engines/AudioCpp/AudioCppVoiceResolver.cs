namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 这个模型只会照参考音频克隆，音色库里又一个音色都没有
/// </summary>
/// <param name="model">模型 id</param>
public sealed class AudioVoiceRequiredException(string model)
    : Exception($"Model '{model}' needs a reference voice from the voice library.")
{
    /// <summary>
    /// 模型 id
    /// </summary>
    public string Model { get; } = model;
}

/// <summary>
/// 抽到的一个音色：模型凭空念的参考句，存进音色库后就能一直用这个声音
/// </summary>
/// <param name="Wav">参考音频</param>
/// <param name="Transcript">念的文字</param>
public sealed record VoiceDraft(byte[] Wav, string Transcript);

/// <summary>
/// 合成时用哪个音色。会克隆却没有自带音色的模型（如 VoxCPM）不给参考音频时，每段文字会随内容换一个说话人，
/// 所以第一次用时让它念一句参考句存进音色库，之后一直照这段克隆，声音就固定了
/// </summary>
internal sealed class AudioCppVoiceResolver
{
    private readonly AudioCppEngine _engine;
    private readonly SemaphoreSlim _seeding = new(1, 1); //同一时刻只生成一个，朗读预取下一句时不重复生成

    public AudioCppVoiceResolver(AudioCppEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// 指定的 → 设置里给这个模型选的 → 模型自带的第一个 → 会克隆的：它自己的固定音色（只会克隆的：音色库第一个）
    /// </summary>
    public async Task<string?> ResolveAsync(AudioCppModelEntry model, string? requested,
        OpenAiCompatibleAudioClient client, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested;
        if (_engine.Settings.TtsVoices.TryGetValue(model.Id, out string? saved)) return saved;
        IReadOnlyList<string> native = await ListNativeAsync(model, client, cancellationToken).ConfigureAwait(false);
        if (native.Count > 0) return native[0];

        AudioCppFamily? family = _engine.GetFamily(model);
        VoiceLibrary? library = _engine.Voices;
        if (family is not { CanClone: true } || library == null) return null;
        if (family.NeedsReference)
            return library.List().FirstOrDefault()?.Name ?? throw new AudioVoiceRequiredException(model.Id);
        return await SeedAsync(model, family, library, client, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 模型自带的音色：服务列出的减去音色库里的（服务会把音色库并进每个模型的列表）
    /// </summary>
    public async Task<IReadOnlyList<string>> ListNativeAsync(AudioCppModelEntry model,
        OpenAiCompatibleAudioClient client, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> voices = await client.GetVoicesAsync(model.Id, cancellationToken).ConfigureAwait(false);
        if (_engine.Voices is not { } library || voices.Count == 0) return voices;
        HashSet<string> names = library.List().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        return voices.Where(x => !names.Contains(x)).ToList();
    }

    /// <summary>
    /// 抽一个音色：不给参考音频，随种子换一句参考句、换一个种子念
    /// </summary>
    public async Task<VoiceDraft> DrawAsync(AudioCppModelEntry model, int seed,
        OpenAiCompatibleAudioClient client, CancellationToken cancellationToken)
    {
        string text = VoiceReferenceText.ForDraw(_engine.GetFamily(model)?.Languages ?? [], seed);
        byte[] wav = await SynthesizeAsync(model, text, seed, client, cancellationToken).ConfigureAwait(false);
        return new VoiceDraft(wav, text);
    }

    // 音色名取家族显示名：同家族的几个包共用一个；库里已有就直接用（用户删了会按同一句重新生成出同一个声音）。
    // 不写进设置：界面上仍是「默认」，指的就是这个声音
    private async Task<string> SeedAsync(AudioCppModelEntry model, AudioCppFamily family, VoiceLibrary library,
        OpenAiCompatibleAudioClient client, CancellationToken cancellationToken)
    {
        await _seeding.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string name = VoiceLibrary.NormalizeName(family.DisplayName) ?? model.Family;
            if (!library.Contains(name))
            {
                string text = VoiceReferenceText.For(family.Languages);
                byte[] wav = await SynthesizeAsync(model, text, null, client, cancellationToken).ConfigureAwait(false);
                library.Save(name, wav, text);
            }

            return name;
        }
        finally
        {
            _seeding.Release();
        }
    }

    private static async Task<byte[]> SynthesizeAsync(AudioCppModelEntry model, string text, int? seed,
        OpenAiCompatibleAudioClient client, CancellationToken cancellationToken)
    {
        AudioSynthesisResult result = await client
            .SynthesizeAsync(model.Id, text, null, EAudioFormat.Wav, cancellationToken, seed).ConfigureAwait(false);
        await using Stream audio = result.Audio;
        using MemoryStream buffer = new();
        await audio.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
