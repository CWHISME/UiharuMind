namespace UiharuMind.Features.Conversation.Pages;

/// <summary>
/// 右栏当前应显示哪个面板。由 <see cref="ConversationPageData.RightPaneKind"/>
/// 唯一决定（智能体恒走完整面板，其次空态，最后跟类型），模板选择只认它。
/// 两类统一懒建：普通对话空态是新建卡（角色在卡上点名字换，工作区段屏蔽）；
/// 智能体空态也是完整面板，能力预演与定时任务无会话时照常有数
/// </summary>
public enum ERightPaneKind
{
    /// <summary>普通对话：会话详情 + 上下文占用</summary>
    Chat,

    /// <summary>智能体：工作区 + 上下文占用 + 能力/子代理/定时任务（含空态）</summary>
    Agent,

    /// <summary>普通对话空态新建卡：状态 + 换角色</summary>
    NewSession,
}
