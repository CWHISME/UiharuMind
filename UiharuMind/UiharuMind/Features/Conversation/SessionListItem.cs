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
/// 唯一有意保留的差异是<b>头像显不显示</b>，那是渲染取舍（工作区页要密度），
/// 因此开关落在 <see cref="SessionListView.ShowAvatar"/> 上而不是本类。
///
/// 会话内容的展示与生成由 <see cref="ConversationViewModel"/> 承载，与本类无关。
/// </summary>
public partial class SessionListItem : ObservableObject
{
    private readonly IMessageService _messageService;
    private ChatSessionMeta _meta;
    private ChatSession? _session;
    private Bitmap? _icon;
    private bool _iconResolved; //头像已解码过(结果可能是 null)
    private string? _characterName;

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

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasDistinctDescription))]
    private string _name;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasDistinctDescription))]
    private string _description;

    [ObservableProperty] private string _timeString;

    /// <summary>
    /// 所属角色的显示名。与头像同样惰性取——构造期不问全局角色库
    /// </summary>
    public string CharacterName => _characterName ??=
        CharacterManager.Instance.GetCharacterData(_meta.CharacterId).CharacterName;

    /// <summary>
    /// 描述是否值得单独占一行。
    ///
    /// 现在只挡「一字不差的重复」。曾经这里按<b>前缀</b>判重，为的是兜住懒建会话
    /// 把标题与描述都取自用户第一句、而标题又截断过的那种情形——那是个错误的层：
    /// 会话一复制（<c>Copy</c> 给标题加后缀）前缀就不再匹配，描述当场冒回来。
    /// 根子已经在 <c>EnsureSessionAsync</c> 修掉：那条路径不再存这份冗余。
    ///
    /// ⚠️ 修之前建的智能体会话仍带着那份冗余的描述，标题超过 30 字的那些会显示出来。
    /// 属于历史数据，改名一次即可，不值得为它保留一个会误伤复制的启发式。
    /// </summary>
    public bool HasDistinctDescription =>
        Description.Length > 0 && !string.Equals(Description, Name, StringComparison.Ordinal);

    /// <summary>
    /// 角色头像。<b>惰性解码</b>——列表可能有成百条，在构造期逐条解码位图会在开页时
    /// 卡住 UI 线程，而实际只有滚进视野的那几条要显示。
    /// 解码一次后不再变：角色换了图标要等下次重开列表，与改之前的行为一致。
    /// </summary>
    public Bitmap? Icon
    {
        get
        {
            if (_iconResolved) return _icon;
            _iconResolved = true;
            _icon = IconUtils.GetCharacterBitmapOrDefault(
                CharacterManager.Instance.GetCharacterData(_meta.CharacterId));
            return _icon;
        }
    }

    /// <summary>本会话有轮次在跑（界面轮次或定时任务的无头轮次）</summary>
    [ObservableProperty] private bool _isRunning;

    /// <summary>本会话有轮次卡在工具审批上等人回应</summary>
    [ObservableProperty] private bool _isAwaitingApproval;

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
        _timeString = CalcTimeString();
        RefreshRunState();
    }

    /// <summary>
    /// 换上新的元数据并刷新显示字段。
    ///
    /// 必须换而不是只刷字段：<c>SaveMeta</c> 往索引里放的是一个<b>新的</b>
    /// <see cref="ChatSessionMeta"/> 对象，条目抓着旧那份就会一直显示旧标题与旧时间。
    /// </summary>
    /// <param name="meta">索引里当前那份元数据</param>
    public void UpdateMeta(ChatSessionMeta meta)
    {
        //换过角色的会话要重取头像与角色名,它们是按角色标识惰性解析的
        bool characterChanged = !string.Equals(_meta.CharacterId, meta.CharacterId, StringComparison.Ordinal);
        _meta = meta;
        if (characterChanged)
        {
            _characterName = null;
            _icon = null;
            _iconResolved = false;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(CharacterName));
        }

        Name = meta.Title;
        Description = meta.Description;
        TimeString = CalcTimeString();
    }

    //================= 运行态 =================

    /// <summary>
    /// 从运行态登记处重读本会话的状态。由页面壳在收到变更通知时调用——
    /// 列表项是随列表刷新反复重建的，让每一项各自订阅全局事件必然漏卸
    /// </summary>
    public void RefreshRunState()
    {
        ESessionRunState state = SessionManager.Instance.Running.StateOf(SessionId);
        IsAwaitingApproval = state == ESessionRunState.AwaitingApproval;
        IsRunning = state == ESessionRunState.Running;
        DeleteCommand.NotifyCanExecuteChanged();
        ClearChatHistoryCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BusyTip));
    }

    /// <summary>
    /// 忙的时候解释删除/清空为何不可用；空闲时为 null，那样就不显示提示
    /// </summary>
    public string? BusyTip => CanMutateFiles
        ? null
        : LocalizationManager.Instance.GetString("SessionBusyCannotModify");

    /// <summary>
    /// 删除与清空历史是否可用。跑的过程中不行：它们会跟正在追写历史的那一轮抢文件
    /// </summary>
    public bool CanMutateFiles => !SessionManager.Instance.Running.IsBusy(SessionId);

    //================= 条目级操作 =================

    [RelayCommand(CanExecute = nameof(CanMutateFiles))]
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

    [RelayCommand(CanExecute = nameof(CanMutateFiles))]
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
