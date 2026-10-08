using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Audio;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 按角色取朗读声音：没设、不是角色说的为空；指的服务或本地模型没了，只留语速与语气、其余跟默认
/// </summary>
public class CharacterSpeechStylesTests
{
    private readonly Dictionary<string, CharacterData> _characters = [];

    [Fact]
    public void StaleTargets_FallBackToDefault_KeepingSpeedAndTone()
    {
        Add("edge", new SpeechStyle { Provider = "service:Edge", Voice = "zh-CN-XiaoyiNeural", Speed = 1.1f });
        Add("gone", new SpeechStyle { Provider = "service:Deleted", Voice = "nova", Instruction = "softly" });
        Add("vox", new SpeechStyle { Provider = AudioCppProvider.LocalId, Model = "vox", Voice = "mine" });
        Add("old", new SpeechStyle { Provider = AudioCppProvider.LocalId, Model = "removed", Voice = "mine", Speed = 0.9f });
        Add("plain", null);
        CharacterSpeechStyles styles = new(id => _characters.GetValueOrDefault(id), id => id == "service:Edge",
            model => model == "vox");

        Assert.Equal("zh-CN-XiaoyiNeural", styles.For("edge")!.Voice);
        Assert.Equal(new SpeechStyle { Instruction = "softly" }, styles.For("gone"));
        Assert.Equal("mine", styles.For("vox")!.Voice);
        Assert.Equal(new SpeechStyle { Speed = 0.9f }, styles.For("old"));
        Assert.Null(styles.For("plain"));
        Assert.Null(styles.For(null));
        Assert.Null(styles.For("nobody"));
    }

    private void Add(string id, SpeechStyle? style) => _characters[id] = new CharacterData { CharacterId = id, Speech = style };
}
