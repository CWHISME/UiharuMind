using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Models.Downloads.Audio;

namespace UiharuMind.App.Tests.Models.Downloads;

/// <summary>
/// 包在家族详情里的短名：spec 的 display_name 写法不一（家族名打头与否、GGUF 在前在后、带括号），都要收成能读的一截
/// </summary>
public class AudioModelTextTests
{
    [Theory]
    [InlineData("PocketTTS", "PocketTTS English Q8_0 GGUF", "English Q8_0")]
    [InlineData("Audio8-ASR-0.1B", "Audio8-ASR-0.1B Q8_0 GGUF", "Q8_0")]
    [InlineData("Audio Flamingo 3 / Next", "Audio Flamingo Next GGUF Q4_K", "Audio Flamingo Next Q4_K")] //不以家族名打头
    [InlineData("F5-TTS", "Vocos mel 24kHz vocoder (GGUF)", "Vocos mel 24kHz vocoder")]
    [InlineData("Qwen3-ASR", "Qwen3-ASR GGUF", "Qwen3-ASR GGUF")] //去完为空时用原名
    [InlineData("Kokoro", "", "kokoro_82m_q8_0")] //没有显示名用 id
    public void PackageLabel_DropsFamilyPrefixAndGguf(string family, string displayName, string expected)
    {
        AudioCppPackage package = new("kokoro_82m_q8_0", "f", EAudioCppTask.Tts, displayName, "q8_0", false, "",
            [], "d", "", [], EAudioCppPackageHost.HuggingFace, "o/r", null, false);

        Assert.Equal(expected, AudioModelText.PackageLabel(package, family));
    }

    [Theory]
    [InlineData(EAudioCppTask.Tts, new[] { "asr", "tts", "clone" }, "AudioCapAlsoAsr")]
    [InlineData(EAudioCppTask.Asr, new[] { "asr", "tts" }, "AudioCapAlsoTts")]
    [InlineData(EAudioCppTask.Tts, new[] { "tts" }, null)]
    public void FeatureTags_MarkTheSecondAbility_First(EAudioCppTask task, string[] tasks, string? expectedKey)
    {
        AudioCppFamily family = new("f", task, "F", "", "supported", [], tasks, [], [], [], "", false, []);

        IReadOnlyList<string> tags = AudioModelText.FeatureTags(family);

        string alsoAsr = UiharuMind.Shared.Services.Loc.Text(UiharuMind.Generated.LangKey.AudioCapAlsoAsr);
        string alsoTts = UiharuMind.Shared.Services.Loc.Text(UiharuMind.Generated.LangKey.AudioCapAlsoTts);
        if (expectedKey == null) Assert.DoesNotContain(tags, x => x == alsoAsr || x == alsoTts);
        else Assert.Equal(UiharuMind.Shared.Services.Loc.Text(expectedKey), tags[0]);
    }
}
