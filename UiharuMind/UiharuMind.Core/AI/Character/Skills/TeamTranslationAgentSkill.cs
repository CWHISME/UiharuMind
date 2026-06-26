using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Character.Skills;

public class TeamTranslationAgentSkill : AgentSkillBase
{
    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args, CancellationToken cancellationToken = default)
    {
        CharacterData translator =
            DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Translator);
        CharacterData reviewer =
            DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.TranslateReviwer);

        return modelRunningData.InvokeSequentialAgentWorkflowStreamingAsync(
            text, translator, reviewer, args, cancellationToken);
    }
}
