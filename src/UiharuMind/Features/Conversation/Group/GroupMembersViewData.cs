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
/// 名单也能改：加人、移出、加回、调整发言顺序（ADR 0046 修订「建群之后增删成员」）。名单是构造时的快照，
/// 改了由群视图按 <see cref="GroupMembership.RosterChanged"/> 换一份新的
/// </summary>
public sealed partial class GroupMembersViewData : ObservableObject
{
    private readonly ChatSession _group;
    private readonly GroupChangePrompts _prompts;
    private GroupHostChoice _selectedHost;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="prompts">改群前的两道关（空闲、跑过先确认）</param>
    internal GroupMembersViewData(ChatSession group, GroupChangePrompts prompts)
    {
        _group = group;
        _prompts = prompts;
        GroupRoster roster = GroupRoster.Of(group);
        int last = roster.Present.Count - 1;
        Members = roster.Present
            .Select((x, i) => new GroupMemberItem(x.Meta, x.SessionId == group.GroupHostSessionId,
                item => _ = RemoveAsync(item), Move)
            {
                CanMoveUp = i > 0,
                CanMoveDown = i < last,
            })
            .ToList();
        FormerMembers = roster.Former
            .Select(x => new GroupFormerMemberItem(x.Meta, character => _ = AddAsync(character)))
            .ToList();
        foreach (GroupMemberItem member in Members) member.Usage.PropertyChanged += OnMemberUsageChanged;
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
            long total = Members.Sum(x => x.Usage.SpentTokens);
            return total > 0 ? string.Format(Loc.Text(LangKey.GroupTotalCostFormat), TurnUsageLedger.Format(total)) : "";
        }
    }

    /// <summary>有没有累计可显示</summary>
    public bool HasTotalCost => Members.Any(x => x.Usage.HasCost);

    /// <summary>主持人可选项：「无」+ 各成员</summary>
    public IReadOnlyList<GroupHostChoice> HostOptions { get; }

    /// <summary>
    /// 当前主持人。用户在下拉里亲手选定（关下拉提交，见面板后台代码）才走到这里：
    /// 跑过的群先确认再写回群壳，成员名字旁的徽章跟着挪。
    /// 下拉的选中是单向显示，切会话/模板重建时控件的抖动到不了这里。
    /// </summary>
    public GroupHostChoice? SelectedHost
    {
        get => _selectedHost;
        set
        {
            // 下拉重建模板时会先推一次 null，不当成「改成无」。
            // 身份只认 SessionId：同一个人改名后选项实例换了名字，记录相等会误判成「换人」。
            if (value == null || value.SessionId == _selectedHost.SessionId) return;
            // 推回来的是落盘真相（取消时的回滚、模板重建时的抖动）：静默对齐，不弹确认。
            // 否则取消那次回推会被当成又一次换主持人，弹窗套弹窗。
            if (value.SessionId == _group.GroupHostSessionId)
            {
                AlignHostChoice(value.SessionId);
                return;
            }
            GroupHostChoice previous = _selectedHost;
            _selectedHost = LiveChoice(value.SessionId);
            OnPropertyChanged();
            // 还没跑过的群没有缓存可失效，直接写回，不弹确认。
            // 跑没跑过问落盘真相：成员行的累计是异步预演后才填上，刚打开那一会儿全是 0。
            if (!GroupChangePrompts.HasRun(_group)) ApplyHost(value);
            else _ = ConfirmHostChangeAsync(previous, value);
        }
    }

    /// <summary>
    /// 换主持人要改写全员系统提示，各自下一轮整个上下文按全价重算（前缀缓存失效），所以先确认。
    /// 下拉绑定是同步的、弹窗是异步的：先让框里显示新值，取消再推回去。
    /// </summary>
    private async Task ConfirmHostChangeAsync(GroupHostChoice previous, GroupHostChoice next)
    {
        bool confirmed = await _prompts.ConfirmIfRunAsync(true,
            Loc.Text(LangKey.GroupHostChangeConfirm, next.Name));
        // 弹窗期间用户又换了一次：以最新那次为准，这次什么都不做（那次有自己的确认）
        if (_selectedHost.SessionId != next.SessionId) return;
        if (!confirmed)
        {
            AlignHostChoice(previous.SessionId);
            return;
        }

        ApplyHost(next);
    }

    /// <summary>把下拉与徽章都对齐到指定的主持人（落盘真相或取消回滚），不写盘不弹窗</summary>
    /// <param name="hostSessionId">主持人的成员会话标识；null 为无主持人</param>
    private void AlignHostChoice(string? hostSessionId)
    {
        _selectedHost = LiveChoice(hostSessionId);
        OnPropertyChanged(nameof(SelectedHost));
        foreach (GroupMemberItem member in Members) member.IsHost = member.SessionId == hostSessionId;
    }

    /// <summary>取当前选项列表里的那一份（同 SessionId）：下拉绑定的选中项必须是列表里的实例</summary>
    /// <param name="hostSessionId">主持人的成员会话标识；null 为无主持人</param>
    /// <returns>列表里的对应项；找不到回「无」</returns>
    private GroupHostChoice LiveChoice(string? hostSessionId) =>
        HostOptions.FirstOrDefault(x => x.SessionId == hostSessionId) ?? HostOptions[0];

    private void ApplyHost(GroupHostChoice value)
    {
        _group.GroupHostSessionId = value.SessionId;
        _group.SaveMeta(touchUpdatedAt: false);
        _selectedHost = LiveChoice(value.SessionId);
        OnPropertyChanged(nameof(SelectedHost));
        foreach (GroupMemberItem member in Members) member.IsHost = member.SessionId == value.SessionId;
    }

    /// <summary>加人：打开挑人弹窗</summary>
    [RelayCommand]
    private Task AddMembers() => AddAsync(null);

    /// <summary>
    /// 加人或加回。群在跑也能加（ADR 0046 修订）：正在说的人下一轮才知道新人，确认里明说。
    /// 挑完先确认，要摘要就请一位成员写（写不成按不补）
    /// </summary>
    private async Task AddAsync(CharacterData? preselected)
    {
        if (await GroupAddMembersWindow.ShowAsync(_group, preselected) is not { } request) return;

        string names = string.Join(Loc.Text(LangKey.GroupSpeakerSeparator), request.Members.Select(x => x.CharacterName));
        string confirm = GroupChangePrompts.WithCacheNote(true, Loc.Text(LangKey.GroupAddConfirmFormat, names));
        bool running = GroupChatCoordinator.Instance.IsRunning(_group.SessionId);
        if (running) confirm += "\n\n" + Loc.Text(LangKey.GroupAddWhileRunningNote);
        if (!await _prompts.ConfirmIfRunAsync(HasTotalCost || running, confirm)) return;

        GroupRoster roster = GroupRoster.Of(_group);
        List<(CharacterData Character, string? ModelName, ChatSession? Former)> picks = request.Members
            .Select((character, i) => (character, request.ModelNames[i],
                roster.FormerOf(character.CharacterId) is { } former
                    ? SessionManager.Instance.Load(former.SessionId)
                    : null))
            .ToList();
        GroupBackfill joiner = request.Backfill == EGroupBackfill.Full ? GroupBackfill.Full : GroupBackfill.None;
        GroupBackfill returner = joiner;
        if (request.Backfill == EGroupBackfill.Summary)
        {
            if (picks.Any(x => x.Former == null)) joiner = await WriteBriefingAsync(returning: false);
            if (picks.Any(x => x.Former != null)) returner = await WriteBriefingAsync(returning: true);
        }

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
    }

    private async Task<GroupBackfill> WriteBriefingAsync(bool returning)
    {
        if (GroupMembership.PickBriefingWriter(_group, id => SessionManager.Instance.Load(id)) is { } writer)
            _prompts.Notify(Loc.Text(LangKey.GroupBriefingWriting, writer.CharacterData.CharacterName));
        if (await GroupMembership.WriteBriefingAsync(_group, returning) is { } briefing) return briefing;

        _prompts.Notify(Loc.Text(LangKey.GroupBriefingFailed), MessageSeverity.Warning);
        return GroupBackfill.None;
    }

    /// <summary>
    /// 移出一位：会话留在「已退出」里。一定确认（这是个动作，不只是缓存的事），跑过的群再补一句代价
    /// </summary>
    private async Task RemoveAsync(GroupMemberItem item)
    {
        if (Members.Count <= GroupMembership.MinMembers)
        {
            _prompts.Notify(Loc.Text(LangKey.GroupRemoveLastMember), MessageSeverity.Warning);
            return;
        }

        if (!_prompts.EnsureIdle(_group)) return;
        string message = Loc.Text(LangKey.GroupRemoveConfirm, item.Name);
        if (item.IsHost) message += "\n" + Loc.Text(LangKey.GroupRemoveHostNote);
        if (!await _prompts.ConfirmAsync(HasTotalCost, message)) return;
        if (!_prompts.EnsureIdle(_group)) return;
        if (SessionManager.Instance.Load(item.SessionId) is not { } member) return;

        if (GroupMembership.Remove(_group, member) == EGroupRosterEdit.Busy)
            _prompts.Notify(Loc.Text(LangKey.GroupEditBusy), MessageSeverity.Warning);
    }

    /// <summary>
    /// 挪发言顺序：不动系统提示（场景段按入群先后列人），不必确认。
    /// 成员私聊不影响顺序，只有跑着一波时改不动
    /// </summary>
    private void Move(GroupMemberItem item, int offset)
    {
        if (GroupMembership.Move(_group, item.SessionId, offset) == EGroupRosterEdit.Busy)
            _prompts.Notify(Loc.Text(LangKey.GroupEditBusy), MessageSeverity.Warning);
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

    private void OnMemberUsageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SessionUsageStats.SpentTokens)) return;
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
    private readonly Action<GroupMemberItem, int> _move;

    /// <summary>角色描述：副标题的主行，空时折叠（不占位）。模型行内不再出现，只进整卡 tooltip</summary>
    public string Description => _character.Description?.Trim() ?? "";

    /// <summary>有没有可显示的角色描述</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>模型用量（底部统计行、进度条与整卡 tooltip）</summary>
    public SessionUsageStats Usage { get; }

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="meta">成员会话的元数据</param>
    /// <param name="isHost">是不是本群主持人</param>
    /// <param name="remove">移出群聊（要确认，交给成员列表）</param>
    /// <param name="move">挪发言顺序（第二个参数负数往前、正数往后，交给成员列表）</param>
    public GroupMemberItem(ChatSessionMeta meta, bool isHost, Action<GroupMemberItem> remove,
        Action<GroupMemberItem, int> move)
    {
        SessionId = meta.SessionId;
        IsHost = isHost;
        _meta = meta;
        _remove = remove;
        _move = move;
        _character = SessionManager.CharacterOf(meta);
        Usage = new SessionUsageStats(meta.SessionId);
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
                    AgentBuildProfile.FromDraft(_character, GroupChatSessions.WorkspaceOf(_meta), GroupChatSessions.PermissionOf(_meta),
                        SessionManager.IsAgentSide(_meta), scene));
            });
            Usage.FixedTokens = snapshot.EstimatedTokens;
        }
        catch
        {
            // 停在占位符
        }

        RefreshUsage();
    }

    /// <summary>重读成员会话的用量</summary>
    public void RefreshUsage() => Usage.Refresh();

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

    /// <summary>不是第一位，能往前挪</summary>
    public bool CanMoveUp { get; init; }

    /// <summary>不是最后一位，能往后挪</summary>
    public bool CanMoveDown { get; init; }

    /// <summary>发言顺序往前挪一位</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _move(this, -1);

    /// <summary>发言顺序往后挪一位</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _move(this, 1);
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
