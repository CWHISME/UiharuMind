using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>从单聊开群带进去的背景：草稿长什么样、交给模型写摘要的是哪一段历史</summary>
public class GroupBackgroundTests
{
    [Fact]
    public void Compose_SaysWhereItCameFrom_AndLeavesRoomForTheQuestion()
    {
        string draft = GroupBackground.Compose("初春饰利", "  用户想定单聊拉人的规则。 ");

        Assert.StartsWith("（背景：这个群是从我和初春饰利的单聊开出来的", draft);
        Assert.Contains("\n\n用户想定单聊拉人的规则。\n\n", draft);
        Assert.EndsWith("\n\n", draft);
    }

    [Fact]
    public void SuppliedHistory_StartsFromTheLastHandoff_AndSkipsKnowledge()
    {
        ChatMessage knowledge = new(ChatRole.User, "检索片段");
        knowledge.AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Knowledge] = true };
        ChatMessage handoff = HistoryHandoff.CreateNote("之前聊过的");
        ChatMessage ask = new(ChatRole.User, "接着说");
        List<ChatMessage> history = [new(ChatRole.User, "很早的话"), handoff, knowledge, ask];

        IReadOnlyList<ChatMessage> supplied = GroupBackground.SuppliedHistory(history, null);

        Assert.Equal([handoff, ask], supplied);
    }

    [Fact]
    public void SuppliedHistory_WithoutTools_DropsToolContent()
    {
        ChatMessage call = new(ChatRole.Assistant, [new FunctionCallContent("c1", "Read")]);
        ChatMessage result = new(ChatRole.Tool, [new FunctionResultContent("c1", "内容")]);
        ChatMessage answer = new(ChatRole.Assistant, "看过了");
        List<ChatMessage> history = [new(ChatRole.User, "看一下"), call, result, answer];

        Assert.Equal(2, GroupBackground.SuppliedHistory(history, null).Count);
        ChatOptions withTools = new() { Tools = [AIFunctionFactory.Create(() => "x", "Read")] };
        Assert.Equal(4, GroupBackground.SuppliedHistory(history, withTools).Count);
    }
}
