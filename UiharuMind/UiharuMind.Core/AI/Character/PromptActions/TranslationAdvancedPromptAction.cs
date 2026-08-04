using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Character.PromptActions;

public class TranslationAdvancedPromptAction : PromptActionConvertableBase
{
    protected override IAsyncEnumerable<string> OnRunAsync(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        TrySetParams("user_request", "None");
        return RunTransientAsync(text, args, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 设置额外需求
    /// </summary>
    /// <param name="extraRequest"></param>
    public void SetExtraRequest(string extraRequest)
    {
        SetParams("user_request", extraRequest);
    }
    
    public override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.TranslatorAdvanced);
    }
}
