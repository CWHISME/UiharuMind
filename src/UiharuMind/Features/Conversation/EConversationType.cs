namespace UiharuMind.Features.Conversation;

/// <summary>
/// 对话的形态（页面左栏切换器的那两档 + 会话自身存的形态，ADR 0050）。
/// 区别于角色的 <c>IsAgent</c>：那是角色身上的身份轴；这是<b>会话层面</b>的轴——
/// 建会话时默认跟角色身份，但从普通对话侧发起可显式定为 <c>Chat</c>（agent 卡开成普通对话）。
/// 页面侧由左栏切换器唯一决定（<c>ConversationPageData.CurrentType</c>）；
/// 会话侧存于 <c>ChatSession.IsAgentForm</c>。
/// 判定进哪一侧列表经 <c>SessionManager.IsAgentSide</c> / <c>IsChatSide</c>，
/// 调用方不手写 <c>!IsAgent</c>——那会把用户卡算进普通对话。
/// </summary>
public enum EConversationType
{
    /// <summary>普通对话：不装工具、不绑工作区的会话</summary>
    Chat,

    /// <summary>智能体：装配工具与工作目录的会话</summary>
    Agent,
}