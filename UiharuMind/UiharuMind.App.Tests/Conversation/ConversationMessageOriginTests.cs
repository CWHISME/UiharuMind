using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 渲染归属：谁由内容流画，谁只能从历史画。
///
/// 这是一轮跑起来之后界面不重不漏的全部依据——分错一边就是静默的重复或缺失，
/// 所以判据只留一份，并且把它钉住。
/// </summary>
public class ConversationMessageOriginTests
{
    [Fact]
    public void AssistantAndToolMessagesComeFromTheContentStream()
    {
        Assert.Equal(EHistoryItemKind.StreamContents,
            ConversationMessageOrigin.KindOf(new ChatMessage(ChatRole.Assistant, "回复")));
        Assert.Equal(EHistoryItemKind.StreamContents,
            ConversationMessageOrigin.KindOf(new ChatMessage(ChatRole.Tool, "结果")));
    }

    [Fact]
    public void UserInputOnlyComesFromHistory()
    {
        EHistoryItemKind kind = ConversationMessageOrigin.KindOf(new ChatMessage(ChatRole.User, "插一句"));

        Assert.Equal(EHistoryItemKind.UserInput, kind);
        Assert.False(ConversationMessageOrigin.IsProducedByContentStream(kind));
    }

    /// <summary>
    /// 检索片段的角色是 Tool、后续报告的角色是 User——按角色分派会把它们画成
    /// 工具结果与用户气泡，所以这两类必须先于角色被认出来。
    /// </summary>
    [Fact]
    public void AnnotatedMessagesAreRecognisedBeforeTheirRole()
    {
        ChatMessage knowledge = new(ChatRole.Tool, "片段")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Knowledge] = true },
        };
        ChatMessage report = new(ChatRole.User, "子代理的结论")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.SubAgentReport] = "sub-1",
            },
        };

        Assert.Equal(EHistoryItemKind.Knowledge, ConversationMessageOrigin.KindOf(knowledge));
        Assert.Equal(EHistoryItemKind.SubAgentReport, ConversationMessageOrigin.KindOf(report));
    }

    [Fact]
    public void NarrationIsNotAnOrdinaryAssistantMessage()
    {
        ChatMessage narration = new(ChatRole.Assistant, "开场白")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Narration] = true },
        };

        Assert.Equal(EHistoryItemKind.Narration, ConversationMessageOrigin.KindOf(narration));
    }

    [Fact]
    public void HandoffNoteIsItsOwnKind()
    {
        ChatMessage note = HistoryHandoff.CreateNote("交接正文");

        Assert.Equal(EHistoryItemKind.HandoffNote, ConversationMessageOrigin.KindOf(note));
    }

    /// <summary>
    /// 只有内容流那一类归流渲染，其余全归历史。这条互补关系一旦破掉，
    /// 要么有东西被画两遍、要么有东西谁都不画。
    /// </summary>
    [Fact]
    public void ExactlyOneKindComesFromTheContentStream()
    {
        EHistoryItemKind[] fromStream = Enum.GetValues<EHistoryItemKind>()
            .Where(ConversationMessageOrigin.IsProducedByContentStream)
            .ToArray();

        Assert.Equal([EHistoryItemKind.StreamContents], fromStream);
    }
}
