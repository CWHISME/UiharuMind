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

public class ChatPluginBase : ObservableObject
{
    public virtual UserControl View { get; }

    private ChatSessionViewData _chatSessionViewData;

    public void SetSessonData(ChatSessionViewData chatSessionViewData)
    {
        _chatSessionViewData = chatSessionViewData;
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