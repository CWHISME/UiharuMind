using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common.ChatPlugins;

// public abstract class ChatPluginBase<T> : UserControl where T : ChatPluginDataBase, new()
// {
//     private readonly T _data;
//
//     public ChatPluginBase()
//     {
//         _data = new T();
//         DataContext = _data;
//     }
//
//     public void SetSessonData(ChatSessionViewData chatSessionViewData)
//     {
//         _data.SetSessonData(chatSessionViewData);
//     }
// }

public partial class ChatPluginBase : ObservableObject
{
    public virtual UserControl View { get; }

    [ObservableProperty] private ChatSessionViewData _chatSessionCurrentViewData;

    public void SetSessonData(ChatSessionViewData chatSessionViewData)
    {
        ChatSessionCurrentViewData = chatSessionViewData;
    }

    public virtual void OnChatBegin()
    {
    }

    public virtual void OnChatEnd()
    {
    }

    partial void OnChatSessionCurrentViewDataChanged(ChatSessionViewData value)
    {
        OnChatSessionChanged(value);
    }

    protected virtual void OnChatSessionChanged(ChatSessionViewData chatSessionViewData)
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