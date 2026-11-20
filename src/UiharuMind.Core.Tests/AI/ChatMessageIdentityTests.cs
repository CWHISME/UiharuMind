using System;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 缺 <see cref="ChatMessage.MessageId"/> 的老存档消息靠确定性回落键认人。
/// 随机 Guid 只在同一次进程内幂等，换实例就失配——条目身份一换，同一条消息被画两遍。
/// </summary>
public class ChatMessageIdentityTests
{
    private static readonly DateTimeOffset SaidAt = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void MissingId_IsStableAcrossInstances()
    {
        ChatMessage first = new(ChatRole.User, "老存档的提问") { CreatedAt = SaidAt };
        ChatMessage second = new(ChatRole.User, "老存档的提问") { CreatedAt = SaidAt };

        Assert.Equal(ChatMessageIdentity.Ensure(first), ChatMessageIdentity.Ensure(second));
        Assert.StartsWith("local-", first.MessageId);
    }

    /// <summary>有 id 的消息原样保留：服务端给的标识不能被本地回落覆盖</summary>
    [Fact]
    public void ExistingId_IsKeptAsIs()
    {
        ChatMessage message = new(ChatRole.Assistant, "回答") { MessageId = "chatcmpl-abc123" };

        Assert.Equal("chatcmpl-abc123", ChatMessageIdentity.Ensure(message));
    }

    /// <summary>回落键区分角色、时间和正文，但同一条消息上重复调用幂等</summary>
    [Fact]
    public void FallbackId_DistinguishesDifferentMessages()
    {
        ChatMessage question = new(ChatRole.User, "提问") { CreatedAt = SaidAt };
        ChatMessage otherText = new(ChatRole.User, "另一个提问") { CreatedAt = SaidAt };
        ChatMessage otherTime = new(ChatRole.User, "提问") { CreatedAt = SaidAt.AddSeconds(1) };
        ChatMessage otherRole = new(ChatRole.Assistant, "提问") { CreatedAt = SaidAt };

        string first = ChatMessageIdentity.Ensure(question);
        Assert.Equal(first, ChatMessageIdentity.Ensure(question));
        Assert.NotEqual(first, ChatMessageIdentity.Ensure(otherText));
        Assert.NotEqual(first, ChatMessageIdentity.Ensure(otherTime));
        Assert.NotEqual(first, ChatMessageIdentity.Ensure(otherRole));
    }
}
