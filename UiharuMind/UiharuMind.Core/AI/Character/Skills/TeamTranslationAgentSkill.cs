using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using UiharuMind.Core.AI.Core;

#pragma warning disable SKEXP0110

namespace UiharuMind.Core.AI.Character.Skills;

public class TeamTranslationAgentSkill : AgentSkillBase
{
    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        var translator = DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Translator)
            .ToAgent(modelRunningData.Kernel!, args);
        var reviwer = DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.TranslateReviwer)
            .ToAgent(modelRunningData.Kernel!, args);

        return modelRunningData.InvokeAutoChatGroupPlanningStreamingAsync(text,
            new HistoryCountTerminationStrategy(3)
            {
                Agents = [reviwer],
            }, cancellationToken,
            translator, reviwer);
    }

    // private sealed class ApprovalTerminationStrategy : TerminationStrategy
    // {
    //     protected override Task<bool> ShouldAgentTerminateAsync(Agent agent, IReadOnlyList<ChatMessageContent> history,
    //         CancellationToken cancellationToken)
    //         => Task.FromResult(history[^1].Content
    //             ?.Contains("审核通过", StringComparison.OrdinalIgnoreCase) ?? false);
    // }

    public sealed class HistoryCountTerminationStrategy : TerminationStrategy
    {
        private readonly int _count;

        public HistoryCountTerminationStrategy(int count)
        {
            _count = count;
        }

        protected override Task<bool> ShouldAgentTerminateAsync(Agent agent, IReadOnlyList<ChatMessageContent> history,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_count <= history.Count);
        }
    }
}