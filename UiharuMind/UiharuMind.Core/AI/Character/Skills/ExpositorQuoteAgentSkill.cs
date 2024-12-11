using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.Skills;

public class ExpositorQuoteAgentSkill : AgentSkillBase
{
    public ExpositorQuoteAgentSkill(string quoteStr)
    {
        SetParams("quote", quoteStr);
    }

    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        var agent = DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.ExpositorQuote)
            .ToAgent(modelRunningData.Kernel, args);

        ChatHistory chatHistory = new ChatHistory();
        chatHistory.AddMessage(AuthorRole.User, text);
        return modelRunningData.InvokeAgentStreamingAsync(agent, chatHistory, cancellationToken);
    }
}