using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.Features.Conversation.SidePanels;

public partial class ChatParamsPanel : UserControl
{
    public ChatParamsPanel()
    {
        InitializeComponent();
    }
}

/// <summary>会话详情栏的对话参数(角色的执行设置)</summary>
public partial class ChatParamsViewData : ObservableObject
{
    [ObservableProperty] private CharacterData _character = null!;

    /// <summary>切到某会话:参数面板改编辑这个会话所属角色的执行设置</summary>
    /// <param name="session">会话列表条目</param>
    public void SetSession(SessionListItem session)
    {
        Character = session.Session.CharacterData;
    }

    /// <summary>一轮生成开始:把用户刚改过的参数落盘,免得改完不发就丢</summary>
    public void NotifyChatBegin()
    {
        if (Character == null) return;
        if (!Character.Config.ExecutionSettings.IsDirty) return;
        Character.Config.ExecutionSettings.IsDirty = false;
        Character.Save();
    }
}
