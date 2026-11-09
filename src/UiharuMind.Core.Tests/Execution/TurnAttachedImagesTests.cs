using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 「这张图已经在这一轮的用户消息里」的判据（<see cref="TurnAttachedImages"/>）。
/// 判据只认最后一条用户消息：往前翻的轮次、已被折叠或截断的老消息都不算——
/// 那些图的字节早就不在请求里，按历史拒绝会让模型再也看不到它
/// </summary>
public class TurnAttachedImagesTests
{
    private static byte[] Image => TestImages.Png(4, 4);

    private static ChatSession SessionWith(params ChatMessage[] messages)
    {
        ChatSession session = new("test", new CharacterData { CharacterId = "test" }) { IsTransient = true };
        foreach (ChatMessage message in messages) session.History.Add(message);
        return session;
    }

    private static ChatMessage UserWithImage(byte[] image) => new(ChatRole.User,
    [
        new DataContent(image, "image/png"),
        new TextContent("看这张\n\n***\n[Attached file: cat.png]"),
    ]);

    [Fact]
    public void LastUserMessageCarryingTheSameBytes_IsAlreadyVisible()
    {
        ChatSession session = SessionWith(UserWithImage(Image));

        Assert.True(new TurnAttachedImages(() => session, () => true).Contains(Image));
    }

    /// <summary>
    /// 上一轮附的图不算：那一轮已经翻过去了，此时的「已经附加过」与眼前看到的东西无关。
    /// 这一条是判据的立身之本——按历史判会让模型在读老图时被挡住
    /// </summary>
    [Fact]
    public void ImageFromAnEarlierTurn_IsNotCounted()
    {
        ChatSession session = SessionWith(UserWithImage(Image), new ChatMessage(ChatRole.User, "再看看别的"));

        Assert.False(new TurnAttachedImages(() => session, () => true).Contains(Image));
    }

    /// <summary>图在助手消息里（不是用户附的）不算：判据要的是「你这条消息里已经有了」</summary>
    [Fact]
    public void ImageInAnAssistantMessage_IsNotCounted()
    {
        ChatSession session = SessionWith(new ChatMessage(ChatRole.Assistant,
            [new DataContent(Image, "image/png")]));

        Assert.False(new TurnAttachedImages(() => session, () => true).Contains(Image));
    }

    /// <summary>
    /// 这一发的模型看不了图：发送时会把这些字节降级成占位（<c>InlineImageDegrading</c>），
    /// 模型其实看不到它——此时再说「已经在你眼前」就是撒谎，必须放行 Read
    /// </summary>
    [Fact]
    public void ModelThatCannotSeeImages_IsNeverTreatedAsAlreadyVisible()
    {
        ChatSession session = SessionWith(UserWithImage(Image));

        Assert.False(new TurnAttachedImages(() => session, () => false).Contains(Image));
    }

    [Fact]
    public void DifferentBytes_AreNotCounted()
    {
        ChatSession session = SessionWith(UserWithImage(TestImages.Png(8, 8)));

        Assert.False(new TurnAttachedImages(() => session, () => true).Contains(Image));
    }

    [Fact]
    public void NoSession_IsNotCounted()
    {
        Assert.False(new TurnAttachedImages(null, () => true).Contains(Image));
        Assert.False(new TurnAttachedImages(() => null, () => true).Contains(Image));
    }

    [Fact]
    public void EmptyHistory_IsNotCounted()
    {
        Assert.False(new TurnAttachedImages(() => SessionWith(), () => true).Contains(Image));
    }

    /// <summary>
    /// 交接文档之前的图不算：供给从交接起算，那一发请求里根本没有那些字节。
    /// 无输入轮（助手锚点重试、唤醒轮）里「最后一条用户消息」可能就落在交接之前，
    /// 不按供给区间判就会说「已经在你眼前」，而模型其实什么也看不到
    /// </summary>
    [Fact]
    public void ImageBeforeTheHandoffNote_IsNotCounted()
    {
        ChatSession session = SessionWith(UserWithImage(Image), HistoryHandoff.CreateNote("整理过的上下文"),
            new ChatMessage(ChatRole.Assistant, "继续"));

        Assert.False(new TurnAttachedImages(() => session, () => true).Contains(Image));
    }

    /// <summary>交接之后的用户图仍算：它在供给区间里，这一发真的带着它</summary>
    [Fact]
    public void ImageAfterTheHandoffNote_IsCounted()
    {
        ChatSession session = SessionWith(HistoryHandoff.CreateNote("整理过的上下文"), UserWithImage(Image));

        Assert.True(new TurnAttachedImages(() => session, () => true).Contains(Image));
    }

    /// <summary>
    /// 本轮已经跑过工具轮次（后面已挂 tool 结果）就不拦：截断阶段只保留最近两组，
    /// 跑得够长的轮次里本轮开头那条消息可能已被截掉，图确实不在请求里
    /// </summary>
    [Fact]
    public void ImageFromATurnThatAlreadyRanToolRounds_IsNotCounted()
    {
        ChatSession session = SessionWith(UserWithImage(Image),
            new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("c1", FileToolNames.Read, new Dictionary<string, object?>())]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "ok")]));

        Assert.False(new TurnAttachedImages(() => session, () => true).Contains(Image));
    }
}
