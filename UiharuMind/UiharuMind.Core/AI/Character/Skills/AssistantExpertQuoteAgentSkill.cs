using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;
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
        var builder = StringBuilderPool.StringBuilder;
        builder.Append(text);
        builder.AppendLine("\n***\n以下为参考内容：");
        builder.AppendLine(_quoteStr);
        _chatHistory = [new ChatMessageData { Role = ECharacter.User, Content = builder.ToString() }];
        return modelRunningData.InvokeAgentStreamingAsync(GetCharacterData(), _chatHistory, args, cancellationToken);
    }
}
