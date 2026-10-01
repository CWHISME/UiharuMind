using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// 附件托盘要回头问视图模型的事。全部现取：首轮发送时会话还不存在（懒建的会话要到
/// RunTurnAsync 内部才建），而附件解析发生在那之前，详见 <see cref="AttachmentTrayViewData.FlushOwnedFiles"/>。
/// </summary>
public interface IAttachmentTrayHost
{
    /// <summary>当前会话；尚未创建时为 null</summary>
    ChatSession? Session { get; }

    /// <summary>
    /// 会话是 agent 形态（空态看新建默认形态）。agent 形态下内联的图片也带路径引用：
    /// 视觉模型只拿到字节，要交给 <c>GenerateImage</c> 改图时没有路径可传（ADR 0052）
    /// </summary>
    bool IsAgentSession { get; }

    /// <summary>当前会话发图有没有识图工具兜底（按会话形态判，见 ADR 0050）</summary>
    bool HasVisionFallback { get; }
}
