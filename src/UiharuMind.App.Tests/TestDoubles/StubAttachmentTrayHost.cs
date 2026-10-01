using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.Composer;

namespace UiharuMind.App.Tests.TestDoubles;

/// <summary>
/// 附件托盘的宿主替身：没有会话，形态与识图兜底按预设回答
/// </summary>
internal sealed class StubAttachmentTrayHost : IAttachmentTrayHost
{
    public ChatSession? Session => null;

    public bool IsAgentSession { get; set; }

    public bool HasVisionFallback { get; set; }
}
