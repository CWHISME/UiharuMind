/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.ViewModels.ViewData;

public partial class ChatViewItemData : ViewModelBase, IPoolAble
{
    // [ObservableProperty] private bool _isUser;
    [ObservableProperty] private ECharacter _role;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private int? _tokenCount;
    [ObservableProperty] private string? _timestamp;
    [ObservableProperty] private bool _isDone = true;

    private ChatMessageContent? _cachedContent;
    public ChatMessageContent? CachedContent => _cachedContent;

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
        // IsUser = item.Character == ECharacter.User;
        Message = item.Message?.Content;
        Timestamp = item.LocalTimeString;
        _cachedContent = item.Message;
    }

    public void Reset()
    {
        Message = null;
        Timestamp = null;
    }
}