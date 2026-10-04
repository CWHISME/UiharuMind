using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 建群弹窗的数据。群的类型由调用方给定（跟着切换器那一侧），这里只挑名字、成员与调度；
/// 挑成员交给 <see cref="GroupCandidatePicker"/>（与之后加人共用）
/// </summary>
public partial class GroupCreateWindowModel : ObservableObject
{
    private const int MinMembers = 2; //一个人的群就是单聊

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    private string _name = string.Empty;

    /// <summary>调度设置（建群时定初值，之后右栏可改）</summary>
    public GroupScheduleViewData Schedule { get; }

    /// <summary>主持人的选项：第一项是「无」，其余是已选成员（建群时定初值，之后右栏可改）</summary>
    public ObservableCollection<GroupHostOption> HostOptions { get; } = [];

    /// <summary>选着的主持人</summary>
    [ObservableProperty] private GroupHostOption? _selectedHost;

    /// <summary>设计器用</summary>
    public GroupCreateWindowModel() : this(false, null)
    {
    }

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="isAgentGroup">是不是智能体群</param>
    /// <param name="workspacePath">智能体群的工作区；普通群为 null</param>
    /// <param name="preselected">预先勾上的成员（从单聊开群时是原单聊的角色；从群开群时是原群成员）；没有为 null</param>
    /// <param name="name">预填的群名；没有为 null</param>
    /// <param name="memberModelNames">预先勾上的成员各自的模型名，顺序与 <paramref name="preselected"/> 一致；null = 跟随全局</param>
    /// <param name="schedule">预填的调度设置；null 为串行、无主持人</param>
    public GroupCreateWindowModel(bool isAgentGroup, string? workspacePath,
        IReadOnlyList<CharacterData>? preselected = null, string? name = null,
        IReadOnlyList<string?>? memberModelNames = null, GroupSchedule? schedule = null)
    {
        IsAgentGroup = isAgentGroup;
        TypeHint = isAgentGroup
            ? Loc.Text(LangKey.GroupCreateAgentHint,
                string.IsNullOrEmpty(workspacePath) ? Loc.Text(LangKey.GroupCreateNoWorkspace) : workspacePath)
            : Loc.Text(LangKey.GroupCreateChatHint);
        // 预选的是用户正在聊的那一位，屏蔽了也照样列出（他本来就看得见），不然预选会静默落空
        Picker = new GroupCandidatePicker(preselected);
        Picker.PickedChanged += OnPickedChanged;
        Schedule = new GroupScheduleViewData(schedule?.Mode ?? EGroupScheduleMode.Serial,
            schedule?.StopPolicy ?? EGroupStopPolicy.Conservative);
        HostOptions.Add(GroupHostOption.None);
        SelectedHost = GroupHostOption.None;

        Name = name ?? string.Empty;
        for (int i = 0; i < (preselected?.Count ?? 0); i++)
        {
            Picker.Pick(preselected![i].CharacterId,
                memberModelNames != null && i < memberModelNames.Count ? memberModelNames[i] : null);
        }

        // 主持人按成员顺序记（建群请求里同样按下标取）：预选完名单齐了再定，Pick 途中会重建选项
        if (schedule?.HostIndex is { } host && host >= 0 && host < Picked.Count)
        {
            SelectedHost = HostOptions.FirstOrDefault(x => x.Data != null && x.Data == Picked[host]);
        }
    }

    /// <summary>挑成员</summary>
    public GroupCandidatePicker Picker { get; }

    /// <summary>是不是智能体群</summary>
    public bool IsAgentGroup { get; }

    /// <summary>类型说明：智能体群绑哪个工作区；普通群不绑，成员一律以普通对话形态参与</summary>
    public string TypeHint { get; }

    /// <summary>已选成员，顺序即发言顺序</summary>
    public IReadOnlyList<CharacterData> Picked => Picker.Picked;

    /// <summary>发言顺序的一行字</summary>
    public string SpeakingOrder => Picked.Count == 0
        ? Loc.Text(LangKey.GroupCreateOrderEmpty)
        : string.Join(" → ", Picked.Select(x => x.CharacterName));

    /// <summary>建群时的调度设置：主持人按它在已选成员里的位置记</summary>
    public GroupSchedule PickedSchedule => new(Schedule.Mode, Schedule.StopPolicy,
        SelectedHost?.Data is { } host ? Picked.ToList().IndexOf(host) : -1);

    /// <summary>名字不空、至少两位成员</summary>
    public bool CanCreate => !string.IsNullOrWhiteSpace(Name) && Picked.Count >= MinMembers;

    private void OnPickedChanged()
    {
        SyncHostOptions();
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(SpeakingOrder));
    }

    // 主持人只能从已选成员里挑：取消勾选的人正好是主持人，就退回「无」
    private void SyncHostOptions()
    {
        GroupHostOption? selected = SelectedHost;
        while (HostOptions.Count > 1) HostOptions.RemoveAt(HostOptions.Count - 1);
        foreach (CharacterData member in Picked) HostOptions.Add(new GroupHostOption(member));
        SelectedHost = HostOptions.FirstOrDefault(x => x.Data != null && x.Data == selected?.Data) ?? GroupHostOption.None;
    }
}

/// <summary>主持人下拉里的一项</summary>
/// <param name="Data">成员角色；null 为「无主持人」</param>
public sealed record GroupHostOption(CharacterData? Data)
{
    /// <summary>无主持人</summary>
    public static GroupHostOption None { get; } = new((CharacterData?)null);

    /// <summary>显示名</summary>
    public string Name => Data?.CharacterName ?? Loc.Text(LangKey.GroupHostNone);
}

/// <summary>建群请求</summary>
/// <param name="Name">群名</param>
/// <param name="Members">成员，顺序即发言顺序</param>
/// <param name="MemberModelNames">成员各自的模型名，顺序与 <paramref name="Members"/> 一致；null = 跟随全局</param>
/// <param name="Schedule">调度设置</param>
public sealed record GroupCreateRequest(string Name, IReadOnlyList<CharacterData> Members,
    IReadOnlyList<string?> MemberModelNames, GroupSchedule Schedule);
