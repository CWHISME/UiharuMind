using System;
using System.Collections.Generic;
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
/// 主持人写在各成员的系统提示场景段里，各自下一轮开跑时重建装配生效（ADR 0048）
/// </summary>
public sealed class GroupMembersViewData
{
    private readonly ChatSession _group;
    private GroupHostChoice _selectedHost;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="group">群壳会话</param>
    public GroupMembersViewData(ChatSession group)
    {
        _group = group;
        Members = SessionManager.MemberMetasOf(group)
            .Select(meta => new GroupMemberItem(meta, meta.SessionId == group.GroupHostSessionId))
            .ToList();
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

    /// <summary>调度设置</summary>
    public GroupScheduleViewData Schedule { get; }

    /// <summary>主持人可选项：「无」+ 各成员</summary>
    public IReadOnlyList<GroupHostChoice> HostOptions { get; }

    /// <summary>当前主持人。改了即写回群壳，成员名字旁的徽章跟着挪</summary>
    public GroupHostChoice? SelectedHost
    {
        get => _selectedHost;
        set
        {
            // 下拉重建模板时会先推一次 null，不当成「改成无」
            if (value == null || value == _selectedHost) return;
            _selectedHost = value;
            _group.GroupHostSessionId = value.SessionId;
            _group.SaveMeta(touchUpdatedAt: false);
            foreach (GroupMemberItem member in Members) member.IsHost = member.SessionId == value.SessionId;
        }
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
}

/// <summary>成员列表里的一项</summary>
public sealed partial class GroupMemberItem : ObservableObject
{
    private readonly ChatSessionMeta _meta;
    private readonly CharacterData _character;
    private int _fixedTokens; //固定开销：预演一次后缓存，不随历史变
    private long _usageTokens; //当前有效占用：最近一次请求输入与固定开销取大

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

    /// <summary>整卡 tooltip：模型名 + 上下文，模型不再占行内一整行</summary>
    public string CardTip
    {
        get
        {
            string name = EffectiveModelName;
            return string.IsNullOrEmpty(ContextLine)
                ? string.Format(Loc.Text(LangKey.GroupMemberModelTooltipFormat), name, "—")
                : string.Format(Loc.Text(LangKey.GroupMemberModelTooltipFormat), name, ContextLine);
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
    public GroupMemberItem(ChatSessionMeta meta, bool isHost)
    {
        SessionId = meta.SessionId;
        IsHost = isHost;
        _meta = meta;
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
    /// 两个口径各管一截（见 ADR 0009 的有效占用）。会话未跑过时以固定开销为准
    /// </summary>
    public void RefreshUsage()
    {
        ChatSession? session = SessionManager.Instance.Load(SessionId);
        _usageTokens = Math.Max(session?.LastInputTokens ?? 0, _fixedTokens);
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
    [ObservableProperty] private bool _isSpeaking;

    /// <summary>打开他自己的会话：与子会话同一个浮窗，可看可聊</summary>
    [RelayCommand]
    private void Open() => SubSessionWindowOpener.Open(SessionId);
}

/// <summary>右栏主持人下拉的一项</summary>
/// <param name="SessionId">成员会话标识；「无」为 null</param>
/// <param name="Name">显示名</param>
public sealed record GroupHostChoice(string? SessionId, string Name);
