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
using UiharuMind.Features.Characters;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话列表条目：元数据驱动的展示 + 条目级操作（改名/复制/删除/清空历史/编辑角色）。
/// 聊天页与 agent 页共用同一实现——两者的差异原本是「拷贝后各自演化」，
/// 不是产品设计，因此这里不设能力开关。
///
/// 会话内容的展示与生成由 <see cref="ConversationViewModel"/> 承载，与本类无关。
/// </summary>
public partial class SessionListItem : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly ChatSessionMeta _meta;
    private ChatSession? _session;

    /// <summary>会话元数据(对话内容页按它加载本体)</summary>
    public ChatSessionMeta Meta => _meta;

    /// <summary>会话标识(列表比对用它,不必为此加载本体)</summary>
    public string SessionId => _meta.SessionId;

    /// <summary>
    /// 会话本体，首次访问时按需加载。列表展示所需字段全部取自元数据，
    /// 只有条目级操作(清空/改名/复制/编辑角色)才会触发加载。
    /// </summary>
    public ChatSession Session => _session ??=
        SessionManager.Instance.Load(_meta.SessionId) ?? new ChatSession { SessionId = _meta.SessionId };

    [ObservableProperty] private string _name;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _timeString;

    /// <summary>会话内容被就地改写(改名/清空历史)。若该会话正被展示,页面壳据此刷新对话区</summary>
    public event Action<SessionListItem>? Mutated;

    /// <summary>会话已被删除。页面壳据此刷新列表并处理"删的正是当前会话"</summary>
    public event Action<SessionListItem>? Deleted;

    public SessionListItem(ChatSession chatSession)
        : this(chatSession.ToMeta())
    {
        _session = chatSession;
    }

    public SessionListItem(ChatSessionMeta meta)
        : this(meta, App.Services.GetRequiredService<IMessageService>())
    {
    }

    public SessionListItem(ChatSessionMeta meta, IMessageService messageService)
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
        Session.Clear();
        TimeString = "";
        Mutated?.Invoke(this);
    }

    [RelayCommand]
    public void EditCharacter()
    {
        UIManager.ShowEditCharacterWindow(new CharacterInfoViewData(Session.CharacterData),
            x => x.SaveCharacter());
    }

    [RelayCommand]
    public async Task Rename()
    {
        string? result = await UIManager.ShowStringEditWindow(_meta.Title);
        if (string.IsNullOrWhiteSpace(result) || result == _meta.Title) return;

        // 标题是纯显示字段:改名不动文件、不删不加。
        // 索引与本体各存一份,两边都要写——只写本体会让列表在重建索引前显示旧名
        _meta.Title = result;
        Session.Title = result;
        Session.Save();
        Name = result;
        Mutated?.Invoke(this);
    }

    [RelayCommand]
    public void Copy()
    {
        SessionManager.Instance.Copy(Session);
    }

    [RelayCommand]
    public async Task Delete()
    {
        if (!await _messageService.ConfirmAsync(Lang.DeleteTips)) return;
        // 按标识删除,不加载本体
        SessionManager.Instance.Delete(_meta.SessionId);
        Deleted?.Invoke(this);
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
