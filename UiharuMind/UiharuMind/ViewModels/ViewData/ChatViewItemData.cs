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

using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 一个聊天记录中的一条消息
/// </summary>
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
#pragma warning disable SKEXP0001
            if (!string.IsNullOrEmpty(_cachedContent?.AuthorName)) return _cachedContent.AuthorName;
#pragma warning restore SKEXP0001
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

    public Action<ChatViewItemData>? DeleteCallback { get; set; }

    public void SetChatItem(ChatMessage item)
    {
        Role = item.Character;
        // IsUser = item.Character == ECharacter.User;
        Message = item.Message?.Content;
        Timestamp = item.LocalTimeString;
        _cachedContent = item.Message;
    }

    [RelayCommand]
    public void Edit()
    {
        Log.Debug("EditCommand" + Message);
    }

    [RelayCommand]
    public void Delete()
    {
        // Log.Debug("DeleteCommand" + Message);
        DeleteCallback?.Invoke(this);
    }

    public void Reset()
    {
        Message = null;
        Timestamp = null;
    }
}