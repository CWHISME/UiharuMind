using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.PromptActions;

public class NormalPromptAction : PromptActionConvertableBase
{
    protected CharacterData _characterData;

    public NormalPromptAction(CharacterData characterData)
    {
        _characterData = characterData;
    }

    public NormalPromptAction(DefaultCharacter character) : this(
        DefaultCharacterManager.Instance.GetCharacterData(character))
    {
    }

    protected override IAsyncEnumerable<string> OnRunAsync(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        // AddParams("content", text);
        return RunTransientAsync(text, args, cancellationToken: cancellationToken);
        // JsonNode.Parse(ModelReaderWriter.Write(content.InnerContent!))["choices"]![0]!["delta"]!["reasoning_content"]
        // return modelRunningData.InvokeQuickToolPromptStreamingAsync("你好", "你是一只猫");
    }

    public override CharacterData GetCharacterData()
    {
        return _characterData; //DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.AssistantExpert);
    }
}
