using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.Core.Chat;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 对话列表视图模型
/// </summary>
public partial class ChatListViewModel : ViewModelBase
{
    public ObservableCollection<ChatSessionViewData> ChatSessions { get; } = new();

    [ObservableProperty] private ChatSessionViewData _selectedSession;

    public ChatListViewModel()
    {
        foreach (var session in ChatManager.Instance.ChatSessions)
        {
            ChatSessions.Add(new ChatSessionViewData(session));
        }

        if (ChatSessions.Count == 0)
            ChatSessions.Add(new ChatSessionViewData(new ChatSession()
            {
                Name = "Uiharu",
                Description = "Uiharu's chat session"
            }));

        SelectedSession = ChatSessions[0];
    }

    partial void OnSelectedSessionChanged(ChatSessionViewData value)
    {
        value.Active();
    }
}