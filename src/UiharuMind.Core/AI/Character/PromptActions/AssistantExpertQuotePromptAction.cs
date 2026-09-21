using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.PromptActions;

public class AssistantExpertQuotePromptAction : NormalPromptAction
{
    private string _quoteStr;

    public AssistantExpertQuotePromptAction(string quoteStr) : base(DefaultCharacter.AssistantExpertQuote)
    {
        // SetParams("quote", quoteStr);
        _quoteStr = quoteStr;
    }

    protected override IAsyncEnumerable<string> OnRunAsync(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        var builder = StringBuilderPool.StringBuilder;
        builder.Append(text);
        builder.AppendLine("\n***\n以下为参考内容：");
        builder.AppendLine(_quoteStr);
        return RunTransientAsync(builder.ToString(), args, cancellationToken: cancellationToken);
    }
}
