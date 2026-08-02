/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views;

namespace UiharuMind.ViewModels.Chat;

/// <summary>
/// 聊天会话列表条目:元数据驱动的展示 + 条目级操作(改名/复制/删除/清空),
/// 会话内容的展示与生成由 ConversationViewModel 承载,与本类无关。
/// </summary>
public partial class ChatSessionItemViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly ChatSessionMeta _meta;
    private ChatSession? _session;

    /// <summary>会话元数据(对话内容页按它加载本体)</summary>
    public ChatSessionMeta Meta => _meta;

    /// <summary>会话标识(列表比对用它,不必为此加载本体)</summary>
    public string SessionId => _meta.SessionId;

    /// <summary>
    /// 会话本体,首次访问时按需加载。列表展示所需字段全部取自元数据,
    /// 只有条目级操作(清空/改名)才会触发加载。
    /// </summary>
    public ChatSession ChatSession => _session ??=
        SessionManager.Instance.Load(_meta.SessionId) ?? new ChatSession { SessionId = _meta.SessionId };

    [ObservableProperty] private string _name;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _timeString;

    /// <summary>会话内容被就地改写(改名/清空历史)。若该会话正被展示,页面壳据此刷新对话区</summary>
    public event Action<ChatSessionItemViewData>? OnSessionMutated;

    public ChatSessionItemViewData(ChatSession chatSession)
        : this(chatSession.ToMeta())
    {
        _session = chatSession;
    }

    public ChatSessionItemViewData(ChatSessionMeta meta)
        : this(meta, App.Services.GetRequiredService<IMessageService>())
    {
    }

    public ChatSessionItemViewData(ChatSessionMeta meta, IMessageService messageService)
    {
        _messageService = messageService;
        _meta = meta;
        _description = meta.Description;
        _name = meta.Title;
        _icon = IconUtils.GetCharacterBitmapOrDefault(
            CharacterManager.Instance.GetCharacterData(meta.CharacterId));
        _timeString = CalcTimeString();
    }


    //================= 条目级操作 =================

    [RelayCommand]
    public async Task ClearChatHistory()
    {
        if (!await _messageService.ConfirmAsync(Lang.ClearTips)) return;
        ChatSession.Clear();
        TimeString = "";
        OnSessionMutated?.Invoke(this);
    }

    [RelayCommand]
    public void EditCharater()
    {
        UIManager.ShowEditCharacterWindow(new CharacterInfoViewData(ChatSession.CharacterData),
            x => x.SaveCharacter());
    }

    [RelayCommand]
    public async Task Rename()
    {
        var result = await UIManager.ShowStringEditWindow(ChatSession.Title);
        if (string.IsNullOrEmpty(result)) return;

        // 标题是纯显示字段:改名不动文件、不删不加
        ChatSession.Title = result;
        ChatSession.Save();
        Name = result;
        OnSessionMutated?.Invoke(this);
    }

    [RelayCommand]
    public void Copy()
    {
        SessionManager.Instance.Copy(ChatSession);
    }

    [RelayCommand]
    public async Task Delete()
    {
        if (await _messageService.ConfirmAsync(Lang.DeleteAllClipboardHistoryTips))
            SessionManager.Instance.Delete(ChatSession);
    }


    private string CalcTimeString()
    {
        // 已加载则用末条消息时间,否则用元数据的更新时间——不为一行时间去加载本体
        DateTime lastTime = _session?.LastTime ?? _meta.UpdatedAt.LocalDateTime;
        return DateTime.Now.Date == lastTime.Date
            ? lastTime.ToString("HH:mm")
            : lastTime.ToString("yyyy/MM/dd");
    }
}
