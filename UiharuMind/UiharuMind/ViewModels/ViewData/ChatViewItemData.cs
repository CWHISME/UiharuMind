using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.ViewModels.ViewData;

public partial class ChatViewItemData : ViewModelBase, IPoolAble
{
    [ObservableProperty] private AuthorRole _role;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string? _timestamp;

    public string SenderIcon => "None";

    public string SenderName
    {
        get
        {
            if (Role == AuthorRole.System) return "System";
            if (Role == AuthorRole.User) return "User";
            if (Role == AuthorRole.Assistant) return "Assistant";
            if (Role == AuthorRole.Tool) return "Tool";
            return "Unknown";
        }
    }

    public IBrush SenderColor
    {
        get
        {
            if (Role == AuthorRole.System) return Brushes.Gray;
            if (Role == AuthorRole.User) return Brushes.LightGreen;
            if (Role == AuthorRole.Assistant) return Brushes.DeepSkyBlue;
            if (Role == AuthorRole.Tool) return Brushes.MediumPurple;
            return Brushes.Black;
        }
    }

    public void SetChatItem(ChatMessage item)
    {
        Role = item.Message.Role;
        Message = item.Message.Content;
        Timestamp = item.LocalTimeString;
    }

    public void Reset()
    {
        Message = null;
        Timestamp = null;
    }
}