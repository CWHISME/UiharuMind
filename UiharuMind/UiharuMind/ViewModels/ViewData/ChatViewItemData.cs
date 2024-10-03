using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.ViewModels.ViewData;

public partial class ChatViewItemData : ViewModelBase, IPoolAble
{
    [ObservableProperty] private ECharacter _role;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string? _timestamp;

    public string SenderIcon => "None";

    public bool IsUser => Role == ECharacter.User;

    public string SenderName
    {
        get
        {
            if (Role == ECharacter.System) return "System";
            if (Role == ECharacter.User) return "User";
            if (Role == ECharacter.Assistant) return "Assistant";
            if (Role == ECharacter.Tool) return "Tool";
            return "Unknown";
        }
    }

    public IBrush SenderColor
    {
        get
        {
            if (Role == ECharacter.System) return Brushes.Gray;
            if (Role == ECharacter.User) return Brushes.LightGreen;
            if (Role == ECharacter.Assistant) return Brushes.DeepSkyBlue;
            if (Role == ECharacter.Tool) return Brushes.MediumPurple;
            return Brushes.Black;
        }
    }

    public void SetChatItem(ChatMessage item)
    {
        Role = item.Character;
        Message = item.Message.Content;
        Timestamp = item.LocalTimeString;
    }

    public void Reset()
    {
        Message = null;
        Timestamp = null;
    }
}