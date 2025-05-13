using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI.Character.Skills;

public class AssistantExpertQuoteAgentSkill : NormalAgentSkill
{
    private string _quoteStr;

    public AssistantExpertQuoteAgentSkill(string quoteStr) : base(DefaultCharacter.AssistantExpertQuote)
    {
        // SetParams("quote", quoteStr);
        _quoteStr = quoteStr;
    }

    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        var agent = GetCharacterData().ToAgent(modelRunningData.Kernel!, args);

        _chatHistory = new ChatHistory();
        _chatHistory.AddMessage(AuthorRole.User, text);
        _chatHistory.AddMessage(AuthorRole.User,"参考内容：\n"+ _quoteStr);
        return modelRunningData.InvokeAgentStreamingAsync(agent, _chatHistory, cancellationToken);
    }
}