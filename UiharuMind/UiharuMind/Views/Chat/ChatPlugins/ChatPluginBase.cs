using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.ViewModels.Chat;

namespace UiharuMind.Views.Common.ChatPlugins;

public partial class ChatPluginBase : ObservableObject
{
    public virtual UserControl View => null!;

    [ObservableProperty] private ChatSessionItemViewData _chatSessionCurrentViewData = null!;

    /// <summary>
    /// 设置插件面板对应的会话条目
    /// </summary>
    /// <param name="chatSessionItemViewData">会话列表条目</param>
    public void SetSessionData(ChatSessionItemViewData chatSessionItemViewData)
    {
        ChatSessionCurrentViewData = chatSessionItemViewData;
    }

    public virtual void OnChatBegin()
    {
    }

    public virtual void OnChatEnd()
    {
    }

    partial void OnChatSessionCurrentViewDataChanged(ChatSessionItemViewData value)
    {
        OnChatSessionChanged(value);
    }

    protected virtual void OnChatSessionChanged(ChatSessionItemViewData chatSessionItemViewData)
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
