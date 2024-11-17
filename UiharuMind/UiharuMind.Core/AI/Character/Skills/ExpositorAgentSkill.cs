using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Character.Skills;

public class ExpositorAgentSkill : AgentSkill
{
    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        // AddParams("content", text);
        var agent = DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.Expositor)
            .ToAgent(modelRunningData.Kernel, args);

        ChatHistory chatHistory = new ChatHistory();
        chatHistory.AddMessage(AuthorRole.User, text);
        return modelRunningData.InvokeAgentStreamingAsync(agent, chatHistory, cancellationToken);
    }
}