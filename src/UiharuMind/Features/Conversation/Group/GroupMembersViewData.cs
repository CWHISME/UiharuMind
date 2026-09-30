using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 群的右栏成员列表：按发言顺序列出成员，点开是他自己的会话（ADR 0046 决策 1：成员是真会话）。
/// 装载后由 <see cref="MarkSpeaking"/> 跟着调度器的发言人变化刷新，不重建实例。
/// 顶上是调度设置与主持人，改了即写回群壳：调度下一波起生效（ADR 0049 决策 1），
/// 主持人写在各成员的系统提示场景段里，各自下一轮开跑时重建装配生效（ADR 0048）。
/// 名单也能改：加人、移出、加回（ADR 0046 修订「建群之后增删成员」），改完整块重建
/// </summary>
public sealed partial class GroupMembersViewData : ObservableObject
{
    private readonly ChatSession _group;
    private readonly Action _rosterChanged; //名单变了：群卡、成员列表、@ 补全整块重建
    private GroupHostChoice _selectedHost;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="rosterChanged">名单变了之后调用（重建群视图）</param>
    public GroupMembersViewData(ChatSession group, Action rosterChanged)
    {
        _group = group;
        _rosterChanged = rosterChanged;
        Members = SessionManager.MemberMetasOf(group)
            .Select(meta => new GroupMemberItem(meta, meta.SessionId == group.GroupHostSessionId,
                item => _ = RemoveAsync(item)))
            .ToList();
        FormerMembers = GroupMembership.FormerMetasOf(group)
            .Select(meta => new GroupFormerMemberItem(meta, character => _ = AddAsync(character)))
            .ToList();
        foreach (GroupMemberItem member in Members) member.PropertyChanged += OnMemberPropertyChanged;
        HostOptions = [new GroupHostChoice(null, Loc.Text(LangKey.GroupHostNone)),
            ..Members.Select(x => new GroupHostChoice(x.SessionId, x.Name))];
        _selectedHost = HostOptions.FirstOrDefault(x => x.SessionId == group.GroupHostSessionId) ?? HostOptions[0];
        Schedule = new GroupScheduleViewData(group.GroupScheduleMode, group.GroupStopPolicy, schedule =>
        {
            group.GroupScheduleMode = schedule.Mode;
            group.GroupStopPolicy = schedule.StopPolicy;
            group.SaveMeta(touchUpdatedAt: false);
        });
    }

    /// <summary>成员，顺序即发言顺序</summary>
    public IReadOnlyList<GroupMemberItem> Members { get; }

    /// <summary>已退出的成员：会话留着，可点开看、可加回</summary>
    public IReadOnlyList<GroupFormerMemberItem> FormerMembers { get; }

    /// <summary>有没有退群的人（「已退出」区据此显隐）</summary>
    public bool HasFormerMembers => FormerMembers.Count > 0;

    /// <summary>「已退出」区的标题</summary>
    public string FormerMembersTitle => Loc.Text(LangKey.GroupFormerMembersFormat, FormerMembers.Count);

    /// <summary>调度设置</summary>
    public GroupScheduleViewData Schedule { get; }

    /// <summary>全群累计 token（各成员会话累计输入 + 输出之和，方案 v6 §3.4 成本可见）；没花过为空</summary>
    public string TotalCostLine
    {
        get
        {
            long total = Members.Sum(x => x.SpentTokens);
            return total > 0 ? string.Format(Loc.Text(LangKey.GroupTotalCostFormat), TurnUsageLedger.Format(total)) : "";
        }
    }

    /// <summary>有没有累计可显示</summary>
    public bool HasTotalCost => Members.Any(x => x.SpentTokens > 0);

    /// <summary>主持人可选项：「无」+ 各成员</summary>
    public IReadOnlyList<GroupHostChoice> HostOptions { get; }

    /// <summary>当前主持人。改了先确认再写回群壳，成员名字旁的徽章跟着挪</summary>
    public GroupHostChoice? SelectedHost
    {
        get => _selectedHost;
        set
        {
            // 下拉重建模板时会先推一次 null，不当成「改成无」
            if (value == null || value == _selectedHost) return;
            GroupHostChoice previous = _selectedHost;
            _selectedHost = value;
            OnPropertyChanged();
            // 还没跑过的群没有缓存可失效，直接写回，不弹确认
            if (!HasTotalCost) ApplyHost(value);
            else _ = ConfirmHostChangeAsync(previous, value);
        }
    }

    /// <summary>
    /// 换主持人要改写全员系统提示，各自下一轮整个上下文按全价重算（前缀缓存失效），所以先确认。
    /// 下拉绑定是同步的、弹窗是异步的：先让框里显示新值，取消再推回去。
    /// </summary>
    private async Task ConfirmHostChangeAsync(GroupHostChoice previous, GroupHostChoice next)
    {
        bool confirmed = await GroupChangePrompts.ConfirmIfRunAsync(true,
            Loc.Text(LangKey.GroupHostChangeConfirm, next.Name));
        // 弹窗期间用户又换了一次：以最新那次为准，这次什么都不做（那次有自己的确认）
        if (_selectedHost != next) return;
        if (!confirmed)
        {
            _selectedHost = previous;
            OnPropertyChanged(nameof(SelectedHost));
            return;
        }

        ApplyHost(next);
    }

    private void ApplyHost(GroupHostChoice value)
    {
        _group.GroupHostSessionId = value.SessionId;
        _group.SaveMeta(touchUpdatedAt: false);
        foreach (GroupMemberItem member in Members) member.IsHost = member.SessionId == value.SessionId;
    }

    /// <summary>加人：打开挑人弹窗</summary>
    [RelayCommand]
    private Task AddMembers() => AddAsync(null);

    /// <summary>
    /// 加人或加回。挑完先确认，要摘要就请一位成员写（写不成按不补），写完再查一次空闲才动名单
    /// </summary>
    private async Task AddAsync(CharacterData? preselected)
    {
        if (!GroupChangePrompts.EnsureIdle(_group)) return;
        if (await GroupAddMembersWindow.ShowAsync(_group, preselected) is not { } request) return;

        string names = string.Join(Loc.Text(LangKey.GroupSpeakerSeparator), request.Members.Select(x => x.CharacterName));
        if (!await GroupChangePrompts.ConfirmIfRunAsync(HasTotalCost,
                GroupChangePrompts.WithCacheNote(true, Loc.Text(LangKey.GroupAddConfirmFormat, names))))
            return;

        List<(CharacterData Character, string? ModelName, ChatSession? Former)> picks = request.Members
            .Select((character, i) => (character, request.ModelNames[i],
                GroupMembership.FormerOf(_group, character.CharacterId) is { } meta
                    ? SessionManager.Instance.Load(meta.SessionId)
                    : null))
            .ToList();
        GroupBackfill joiner = request.Backfill == EGroupBackfill.Full ? GroupBackfill.Full : GroupBackfill.None;
        GroupBackfill returner = joiner;
        if (request.Backfill == EGroupBackfill.Summary)
        {
            if (picks.Any(x => x.Former == null)) joiner = await WriteBriefingAsync(returning: false);
            if (picks.Any(x => x.Former != null)) returner = await WriteBriefingAsync(returning: true);
        }

        // 写摘要要等几秒，期间群可能又跑起来了
        if (!GroupChangePrompts.EnsureIdle(_group)) return;
        foreach ((CharacterData character, string? modelName, ChatSession? former) in picks)
        {
            if (former != null)
            {
                former.SessionModelName = modelName;
                GroupMembership.Admit(_group, former, returner);
            }
            else
            {
                GroupMembership.Join(_group, character, modelName, joiner);
            }
        }

        _rosterChanged();
    }

    private async Task<GroupBackfill> WriteBriefingAsync(bool returning)
    {
        if (GroupMembership.PickBriefingWriter(_group, id => SessionManager.Instance.Load(id)) is { } writer)
            GroupChangePrompts.Notify(Loc.Text(LangKey.GroupBriefingWriting, writer.CharacterData.CharacterName));
        if (await GroupMembership.WriteBriefingAsync(_group, returning) is { } briefing) return briefing;

        GroupChangePrompts.Notify(Loc.Text(LangKey.GroupBriefingFailed), MessageSeverity.Warning);
        return GroupBackfill.None;
    }

    /// <summary>
    /// 移出一位：会话留在「已退出」里。一定确认（这是个动作，不只是缓存的事），跑过的群再补一句代价
    /// </summary>
    private async Task RemoveAsync(GroupMemberItem item)
    {
        if (Members.Count <= GroupMembership.MinMembers)
        {
            GroupChangePrompts.Notify(Loc.Text(LangKey.GroupRemoveLastMember), MessageSeverity.Warning);
            return;
        }

        if (!GroupChangePrompts.EnsureIdle(_group)) return;
        string message = Loc.Text(LangKey.GroupRemoveConfirm, item.Name);
        if (item.IsHost) message += "\n" + Loc.Text(LangKey.GroupRemoveHostNote);
        if (!await GroupChangePrompts.ConfirmAsync(HasTotalCost, message)) return;
        if (!GroupChangePrompts.EnsureIdle(_group)) return;
        if (SessionManager.Instance.Load(item.SessionId) is not { } member) return;

        if (GroupMembership.Remove(_group, member)) _rosterChanged();
    }

    /// <summary>
    /// 把「正在说」标到对应成员上（并行时可能几位同时）；传空则全部熄灭。
    /// 每次发言人变化都顺带刷新占用：成员跑完一轮，<see cref="ChatSession.LastInputTokens"/> 落盘，
    /// 列表跟着更新，不再是建群时的那口初始值
    /// </summary>
    /// <param name="speakerSessionIds">正在发言的成员会话</param>
    public void MarkSpeaking(IReadOnlyCollection<string> speakerSessionIds)
    {
        foreach (GroupMemberItem member in Members) member.IsSpeaking = speakerSessionIds.Contains(member.SessionId);
        foreach (GroupMemberItem member in Members) member.RefreshUsage();
    }

    /// <summary>
    /// 从运行态登记处重读各成员在不在跑、是不是卡在审批上。群轮与私聊轮都由 TurnDriver 登记，
    /// 在跑却不在发言人里的，就是在他自己的窗口里私聊
    /// </summary>
    public void RefreshRunStates()
    {
        foreach (GroupMemberItem member in Members)
        {
            ESessionRunState state = SessionManager.Instance.Running.StateOf(member.SessionId);
            member.IsRunning = state != ESessionRunState.Idle;
            member.IsAwaitingApproval = state == ESessionRunState.AwaitingApproval;
        }
    }

    /// <summary>
    /// 某位成员刚报了一次用量：只刷他那一行
    /// </summary>
    /// <param name="sessionId">成员会话标识</param>
    public void RefreshUsageOf(string sessionId) => Members.FirstOrDefault(x => x.SessionId == sessionId)?.RefreshUsage();

    /// <summary>
    /// 这个会话是不是本群成员
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>是成员为 true</returns>
    public bool Contains(string sessionId) => Members.Any(x => x.SessionId == sessionId);

    private void OnMemberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GroupMemberItem.SpentTokens)) return;
        OnPropertyChanged(nameof(TotalCostLine));
        OnPropertyChanged(nameof(HasTotalCost));
    }
}

/// <summary>成员列表里的一项</summary>
public sealed partial class GroupMemberItem : ObservableObject
{
    private readonly ChatSessionMeta _meta;
    private readonly CharacterData _character;
    private readonly Action<GroupMemberItem> _remove;
    private int _fixedTokens; //固定开销：预演一次后缓存，不随历史变
    private long _usageTokens; //当前有效占用：最近一次请求输入与固定开销取大
    private long _inputTokens; //会话累计输入
    private long _outputTokens; //会话累计输出

    /// <summary>角色描述：副标题的主行，空时折叠（不占位）。模型行内不再出现，只进整卡 tooltip</summary>
    public string Description => _character.Description?.Trim() ?? "";

    /// <summary>有没有可显示的角色描述</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>上下文行：占用 / 模型上限（与右栏上下文占用同一形状；上限未知时只给占用；无占用时为空，由 HasUsage 折叠）</summary>
    public string ContextLine
    {
        get
        {
            if (_usageTokens <= 0) return "";
            int contextLength = ResolveModel()?.ContextLength ?? 0;
            return contextLength > 0
                ? $"{TurnUsageLedger.Format(_usageTokens)} / {TurnUsageLedger.Format(contextLength)}"
                : TurnUsageLedger.Format(_usageTokens);
        }
    }

    /// <summary>累计花费：会话累计输入 + 输出（成本视角，一轮多次工具往返逐次相加）</summary>
    public long SpentTokens => _inputTokens + _outputTokens;

    /// <summary>累计行：与占用同一行靠右；没花过为空，由 HasCost 折叠</summary>
    public string CostLine => HasCost
        ? string.Format(Loc.Text(LangKey.GroupMemberCostFormat), TurnUsageLedger.Format(SpentTokens))
        : "";

    /// <summary>有没有累计可显示</summary>
    public bool HasCost => SpentTokens > 0;

    /// <summary>整卡 tooltip：模型名 + 上下文，花过的再补一行输入 / 输出拆分</summary>
    public string CardTip
    {
        get
        {
            string tip = string.Format(Loc.Text(LangKey.GroupMemberModelTooltipFormat), EffectiveModelName,
                string.IsNullOrEmpty(ContextLine) ? "—" : ContextLine);
            if (!HasCost) return tip;
            return tip + "\n" + string.Format(Loc.Text(LangKey.GroupMemberCostTooltipFormat),
                TurnUsageLedger.Format(_inputTokens), TurnUsageLedger.Format(_outputTokens));
        }
    }

    /// <summary>实际生效的模型名（钉选 → 全局当前 → 首选，不套「跟随全局」包装，专给 tooltip）</summary>
    private string EffectiveModelName
    {
        get
        {
            if (!string.IsNullOrEmpty(_meta.SessionModelName)) return _meta.SessionModelName;
            string? name = LlmManager.Instance.CurrentRunningModel?.ModelName
                           ?? LlmManager.Instance.GetPreferredModelName(false);
            return string.IsNullOrEmpty(name) ? "—" : name;
        }
    }

    /// <summary>占用占上限的百分比（进度条；上限未知或没算出来为 0）</summary>
    public double UsagePercent
    {
        get
        {
            int contextLength = ResolveModel()?.ContextLength ?? 0;
            return contextLength > 0 ? Math.Clamp(_usageTokens * 100.0 / contextLength, 0, 100) : 0;
        }
    }

    /// <summary>有没有算出来的占用（占用行与进度条据此显隐）</summary>
    public bool HasUsage => _usageTokens > 0;

    /// <summary>成员会话的模型（钉选解析 → 全局当前）</summary>
    private ModelRunningData? ResolveModel()
    {
        if (!string.IsNullOrEmpty(_meta.SessionModelName)
            && LlmManager.Instance.CacheModelDictionary.TryGetValue(_meta.SessionModelName, out ModelRunningData? pinned))
            return pinned;
        return LlmManager.Instance.CurrentRunningModel;
    }

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="meta">成员会话的元数据</param>
    /// <param name="isHost">是不是本群主持人</param>
    /// <param name="remove">移出群聊（要确认，交给成员列表）</param>
    public GroupMemberItem(ChatSessionMeta meta, bool isHost, Action<GroupMemberItem> remove)
    {
        SessionId = meta.SessionId;
        IsHost = isHost;
        _meta = meta;
        _remove = remove;
        _character = SessionManager.CharacterOf(meta);
        _ = RefreshStatsAsync();
    }

    /// <summary>
    /// 后台预演一次装配拿到固定开销（与右栏能力统计同一路径，数字可对账，群场景段也算在内），
    /// 随后取会话本体的真实占用。预演失败（角色已删、装配异常）停在占位符，不打断成员列表
    /// </summary>
    private async Task RefreshStatsAsync()
    {
        try
        {
            AgentCapabilitySnapshot snapshot = await Task.Run(() =>
            {
                string scene = SessionManager.Instance.Load(SessionId) is { } session
                    ? GroupSceneSource.For(session)
                    : string.Empty;
                return CharacterRunnerFactory.Instance.PreviewCapabilitiesAsync(
                    AgentBuildProfile.FromDraft(_character, _meta.WorkspacePath, _meta.PermissionModeIndex,
                        SessionManager.IsAgentSide(_meta), scene));
            });
            _fixedTokens = snapshot.EstimatedTokens;
        }
        catch
        {
            // 停在占位符
        }

        RefreshUsage();
    }

    /// <summary>
    /// 取成员会话本体的真实占用（<see cref="ChatSession.LastInputTokens"/>，最近一次请求的输入 token），
    /// 与固定开销取大——固定开销是「每轮最低要吃掉多少」，请求输入是服务端报的实际占用，
    /// 两个口径各管一截（见 ADR 0009 的有效占用）。会话未跑过时以固定开销为准。累计花费同一处取
    /// </summary>
    public void RefreshUsage()
    {
        ChatSession? session = SessionManager.Instance.Load(SessionId);
        _usageTokens = Math.Max(session?.LastInputTokens ?? 0, _fixedTokens);
        _inputTokens = session?.TotalInputTokens ?? 0;
        _outputTokens = session?.TotalOutputTokens ?? 0;
        OnPropertyChanged(nameof(SpentTokens));
        OnPropertyChanged(nameof(CostLine));
        OnPropertyChanged(nameof(HasCost));
        OnPropertyChanged(nameof(ContextLine));
        OnPropertyChanged(nameof(UsagePercent));
        OnPropertyChanged(nameof(HasUsage));
        OnPropertyChanged(nameof(CardTip));
    }

    /// <summary>成员会话标识</summary>
    public string SessionId { get; }

    /// <summary>显示名</summary>
    public string Name => _character.CharacterName;

    /// <summary>是不是本群主持人（名字旁挂徽章）</summary>
    [ObservableProperty] private bool _isHost;

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(_character);

    /// <summary>此刻是不是群里的发言人（右栏标出正在说的那一位）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSpeaking))]
    [NotifyPropertyChangedFor(nameof(ShowsPrivateChat))]
    private bool _isSpeaking;

    /// <summary>他这一轮是不是停着等审批（右栏标「等审批」，点开他的会话能批）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSpeaking))]
    [NotifyPropertyChangedFor(nameof(ShowsPrivateChat))]
    private bool _isAwaitingApproval;

    /// <summary>他的会话此刻有没有轮次在跑（群轮或私聊轮）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPrivateChat))]
    private bool _isRunning;

    /// <summary>标「发言中」：等审批时让位给「等审批」，两个标不同时挂</summary>
    public bool ShowsSpeaking => IsSpeaking && !IsAwaitingApproval;

    /// <summary>标「私聊中」：在跑，但不是群里这一轮——回复只有用户看得到，不进群</summary>
    public bool ShowsPrivateChat => IsRunning && !IsSpeaking && !IsAwaitingApproval;

    /// <summary>打开他自己的会话：与子会话同一个浮窗，可看可聊</summary>
    [RelayCommand]
    private void Open() => SubSessionWindowOpener.Open(SessionId);

    /// <summary>移出群聊</summary>
    [RelayCommand]
    private void Remove() => _remove(this);
}

/// <summary>「已退出」区的一项：会话留着，可点开看、可加回</summary>
public sealed partial class GroupFormerMemberItem
{
    private readonly ChatSessionMeta _meta;
    private readonly CharacterData _character;
    private readonly Action<CharacterData> _rejoin;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="meta">退群成员会话的元数据</param>
    /// <param name="rejoin">加回（走加人弹窗，预先勾上他）</param>
    public GroupFormerMemberItem(ChatSessionMeta meta, Action<CharacterData> rejoin)
    {
        _meta = meta;
        _character = SessionManager.CharacterOf(meta);
        _rejoin = rejoin;
    }

    /// <summary>显示名</summary>
    public string Name => _character.CharacterName;

    /// <summary>头像</summary>
    public Bitmap? Icon => IconUtils.GetCharacterBitmapOrDefault(_character);

    /// <summary>打开他的会话：看他在群里时收到什么、说过什么</summary>
    [RelayCommand]
    private void Open() => SubSessionWindowOpener.Open(_meta.SessionId);

    /// <summary>加回</summary>
    [RelayCommand]
    private void Rejoin() => _rejoin(_character);
}

/// <summary>右栏主持人下拉的一项</summary>
/// <param name="SessionId">成员会话标识；「无」为 null</param>
/// <param name="Name">显示名</param>
public sealed record GroupHostChoice(string? SessionId, string Name);
