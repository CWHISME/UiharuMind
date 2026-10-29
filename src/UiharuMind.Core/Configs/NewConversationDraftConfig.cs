using UiharuMind.Core.Configs;

namespace UiharuMind.Core.Configs;

/// <summary>
/// 「新建会话」空态输入框草稿的持久化载体：会话本体尚不存在（首轮未发），
/// 输入文字挂不到 <c>ChatSession.ComposerDraft</c> 上，由对话页壳收进这份配置，
/// 切走与应用退出前写盘、回到空态时回填（见 <c>EmptySessionDraftStore</c> 与
/// <c>ConversationPageDataBase</c>）。
/// </summary>
public class NewConversationDraftConfig : TConfigBase<NewConversationDraftConfig>
{
    /// <summary>普通对话档「新建」空态的输入草稿</summary>
    public string? ChatDraft { get; set; }

    /// <summary>智能体档「新建」空态的输入草稿</summary>
    public string? AgentDraft { get; set; }
}