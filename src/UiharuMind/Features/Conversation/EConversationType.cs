namespace UiharuMind.Features.Conversation;

/// <summary>
/// 对话的类型（页面左栏切换器的那两档）。区别于角色的 <c>IsAgent</c>：
/// 那是角色身上的身份轴，这是会话层面的二分类——普通对话与智能体，由前者派生。
/// 判定进哪一类一律经 <c>IsChat()</c> / <c>IsAgent</c>，调用方不手写 <c>!IsAgent</c>——那会把用户卡算进普通对话。
/// </summary>
public enum EConversationType
{
    /// <summary>普通对话：普通角色的会话</summary>
    Chat,

    /// <summary>智能体：装配工具与工作目录的会话</summary>
    Agent,
}