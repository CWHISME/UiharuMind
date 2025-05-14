using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Utils;

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
        var builder = StringBuilderPool.StringBuilder;
        builder.Append(text);
        builder.AppendLine("\n***\n以下为参考内容：");
        builder.AppendLine(_quoteStr);
        _chatHistory.AddUserMessage(builder.ToString());
        return modelRunningData.InvokeAgentStreamingAsync(agent, _chatHistory, cancellationToken);
    }
}