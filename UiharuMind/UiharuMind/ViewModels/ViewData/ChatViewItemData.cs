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
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Utils;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 一个聊天记录中的一条消息
/// </summary>
public partial class ChatViewItemData : ViewModelBase, IPoolAble
{
    // [ObservableProperty] private bool _isUser;
    [ObservableProperty] private ECharacter _role;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private Bitmap? _messageImage;
    [ObservableProperty] private int? _tokenCount;
    [ObservableProperty] private string? _timestamp;
    [ObservableProperty] private bool _isDone = true;

    private ChatMessageContent? _cachedContent;
    public ChatMessageContent? CachedContent => _cachedContent;

    public string SenderIcon => "None";

    public bool IsSystem => Role == ECharacter.System;
    public bool IsUser => Role == ECharacter.User;

    // public bool IsImageContent => (_cachedContent?.Items.Count > 0 && _cachedContent.Items[0] is ImageContent);
    //是否是图片
    public bool IsImageContent { get; private set; }

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
        if (item.Message?.Items.Count > 0 && item.Message.Items[0] is ImageContent imageContent &&
            imageContent.DataUri != null)
        {
            IsImageContent = true;
            MessageImage = UiUtils.Base64ToBitmap(imageContent.DataUri);
            if (MessageImage == null)
            {
                IsImageContent = false;
                Message = "[Image] load failed";
            }
        }
    }

    [RelayCommand]
    public async Task Edit()
    {
        var result = await StringContentEditWindow.Show(Message ?? "");
        if (!string.IsNullOrEmpty(result))
        {
            Message = result;
        }
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

    partial void OnMessageChanged(string? value)
    {
        if (_cachedContent == null) return;
        _cachedContent.Content = value;
    }
}