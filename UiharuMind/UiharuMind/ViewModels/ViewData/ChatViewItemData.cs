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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Utils;
using UiharuMind.Views;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 一个聊天记录中的一条消息
/// </summary>
public partial class ChatViewItemData : ObservableObject, IPoolAble
{
    [ObservableProperty] private ChatRole _role;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private Bitmap? _messageImage;
    [ObservableProperty] private int? _tokenCount;
    [ObservableProperty] private string? _timestamp;
    [ObservableProperty] private bool _isDone = true;

    private ChatMessage? _cachedContent;

    /// <summary>底层消息（编辑时就地改写它的文本）</summary>
    public ChatMessage? CachedContent => _cachedContent;

    public string SenderIcon => "None";

    public bool IsSystem => Role == ChatRole.System;
    public bool IsUser => Role == ChatRole.User;

    /// <summary>是否含图片</summary>
    public bool IsImageContent { get; private set; }

    public string SenderName
    {
        get
        {
            if (!string.IsNullOrEmpty(_cachedContent?.AuthorName)) return _cachedContent.AuthorName;
            if (Role == ChatRole.System) return "System";
            if (Role == ChatRole.User) return "User";
            if (Role == ChatRole.Assistant) return "Assistant";
            if (Role == ChatRole.Tool) return "Tool";
            return "Unknown";
        }
    }

    public IBrush SenderColor
    {
        get
        {
            if (Role == ChatRole.System) return Brushes.Gray;
            if (Role == ChatRole.User) return Brushes.LightGreen;
            if (Role == ChatRole.Assistant) return Brushes.DeepSkyBlue;
            if (Role == ChatRole.Tool) return Brushes.MediumPurple;
            return Brushes.Black;
        }
    }

    public Action<ChatViewItemData>? DeleteCallback { get; set; }
    public Action<ChatViewItemData>? RetryCallback { get; set; }
    public Action<ChatViewItemData>? BranchCallback { get; set; }

    /// <summary>
    /// 绑定一条消息
    /// </summary>
    /// <param name="item">消息</param>
    public void SetChatItem(ChatMessage item)
    {
        Role = item.Role;
        Message = item.Text;
        Timestamp = (item.CreatedAt ?? DateTimeOffset.Now).LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss");
        _cachedContent = item;

        DataContent? image = item.Contents.OfType<DataContent>()
            .FirstOrDefault(x => x.HasTopLevelMediaType("image"));
        if (image == null) return;

        IsImageContent = true;
        try
        {
            using MemoryStream stream = new(image.Data.ToArray());
            MessageImage = new Bitmap(stream);
        }
        catch (Exception e)
        {
            Log.Warning($"Load message image failed: {e.Message}");
            IsImageContent = false;
            Message = "[Image] load failed";
        }
    }

    [RelayCommand]
    public async Task Edit()
    {
        var result = await UIManager.ShowStringEditWindow(Message ?? "");
        if (!string.IsNullOrEmpty(result))
        {
            Message = result;
        }
    }

    [RelayCommand]
    public void Delete()
    {
        DeleteCallback?.Invoke(this);
    }

    [RelayCommand]
    public void Copy()
    {
        if (string.IsNullOrEmpty(Message)) return;
        App.Clipboard.CopyToClipboard(Message, true);
    }

    [RelayCommand]
    public void Retry()
    {
        RetryCallback?.Invoke(this);
    }

    [RelayCommand]
    public void Branch()
    {
        BranchCallback?.Invoke(this);
    }

    public void Reset()
    {
        Message = null;
        Timestamp = null;
        IsImageContent = false;
        MessageImage = null;
        _cachedContent = null;
    }

    partial void OnMessageChanged(string? value)
    {
        if (_cachedContent == null) return;
        // ChatMessage.Text 是只读的(它是所有 TextContent 的拼接),改写要落到 TextContent 上,
        // 这样图片等其他内容不受影响
        TextContent? text = _cachedContent.Contents.OfType<TextContent>().FirstOrDefault();
        if (text != null) text.Text = value ?? "";
        else _cachedContent.Contents.Add(new TextContent(value ?? ""));
    }

    partial void OnRoleChanged(ChatRole value)
    {
        OnPropertyChanged(nameof(IsSystem));
        OnPropertyChanged(nameof(IsUser));
    }
}
