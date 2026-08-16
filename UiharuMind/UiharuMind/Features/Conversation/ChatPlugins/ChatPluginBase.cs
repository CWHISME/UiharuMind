using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.SessionList;

namespace UiharuMind.Features.Conversation.ChatPlugins;

public partial class ChatPluginBase : ObservableObject
{
    public virtual UserControl View => null!;

    [ObservableProperty] private SessionListItem _chatSessionCurrentViewData = null!;

    /// <summary>
    /// 设置插件面板对应的会话条目
    /// </summary>
    /// <param name="chatSessionItemViewData">会话列表条目</param>
    public void SetSessionData(SessionListItem chatSessionItemViewData)
    {
        ChatSessionCurrentViewData = chatSessionItemViewData;
    }

    public virtual void OnChatBegin()
    {
    }

    public virtual void OnChatEnd()
    {
    }

    partial void OnChatSessionCurrentViewDataChanged(SessionListItem value)
    {
        OnChatSessionChanged(value);
    }

    protected virtual void OnChatSessionChanged(SessionListItem chatSessionItemViewData)
    {
    }
}

public class ChatPluginDataBase<T> : ChatPluginBase where T : UserControl, new()
{
    private T? _view;

    public override UserControl View
    {
        get
        {
            if (_view == null)
            {
                _view = new T();
                _view.DataContext = this;
            }

            return _view;
        }
    }
}
