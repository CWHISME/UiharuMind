namespace UiharuMind.Features.Conversation;

/// <summary>
/// 对话的类型（页面左栏切换器的那两档）。区别于 <c>ECharacterKind</c>：
/// 那是角色的档位（四档），这是会话层面的二分类——普通对话（扮演/工具人）与智能体。
/// 判定进哪一类一律经 <c>CharacterKindRouting</c>（<c>IsChat()</c> / <c>IsAgent()</c>），
/// 调用方不写裸 <c>Kind == Roleplay</c> 那种四档之后会漏掉工具人的判据。
/// </summary>
public enum EConversationType
{
    /// <summary>普通对话：装扮演与工具人两档的会话</summary>
    Chat,

    /// <summary>智能体：装配工具与工作目录的会话</summary>
    Agent,
}