using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 建群之后加人（含加回退群的人）的弹窗数据：挑人交给 <see cref="GroupCandidatePicker"/>，
/// 已在群里的不列；已退出的挂徽章，勾上恢复他原来的会话。另选补多少群里的历史
/// </summary>
public partial class GroupAddMembersWindowModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackfillNone))]
    [NotifyPropertyChangedFor(nameof(IsBackfillSummary))]
    [NotifyPropertyChangedFor(nameof(IsBackfillFull))]
    private EGroupBackfill _backfill = EGroupBackfill.None;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="preselected">预先勾上的角色（从「已退出」点加回时是他）；没有为 null</param>
    public GroupAddMembersWindowModel(ChatSession group, CharacterData? preselected = null)
    {
        HashSet<string> present = SessionManager.MemberMetasOf(group).Select(x => x.CharacterId).ToHashSet();
        Dictionary<string, string?> formerModels = GroupMembership.FormerMetasOf(group)
            .GroupBy(x => x.CharacterId)
            .ToDictionary(x => x.Key, x => x.First().SessionModelName);
        // 退群的人照样列出，哪怕后来被屏蔽了：他就在这个群的历史里
        List<CharacterData> formerCharacters = formerModels.Keys
            .Select(id => CharacterManager.Instance.GetCharacterData(id))
            .OfType<CharacterData>()
            .ToList();
        Picker = new GroupCandidatePicker(formerCharacters, present, formerModels.Keys.ToHashSet());
        Picker.PickedChanged += () => OnPropertyChanged(nameof(CanAdd));
        // 加回的人默认沿用他原来钉的模型
        if (preselected != null) Picker.Pick(preselected.CharacterId, formerModels.GetValueOrDefault(preselected.CharacterId));

        Hint = Loc.Text(LangKey.GroupAddHint, group.Title);
        FullLabel = Loc.Text(LangKey.GroupBackfillFullFormat, group.History.Count);
    }

    /// <summary>挑成员</summary>
    public GroupCandidatePicker Picker { get; }

    /// <summary>弹窗副标题：加进哪个群、退群的人怎么处理</summary>
    public string Hint { get; }

    /// <summary>「全部补发」那一项的文字，写明最多几条</summary>
    public string FullLabel { get; }

    /// <summary>至少挑了一位</summary>
    public bool CanAdd => Picker.Picked.Count > 0;

    /// <summary>补历史：不补</summary>
    public bool IsBackfillNone
    {
        get => Backfill == EGroupBackfill.None;
        set
        {
            if (value) Backfill = EGroupBackfill.None;
        }
    }

    /// <summary>补历史：摘要</summary>
    public bool IsBackfillSummary
    {
        get => Backfill == EGroupBackfill.Summary;
        set
        {
            if (value) Backfill = EGroupBackfill.Summary;
        }
    }

    /// <summary>补历史：全部</summary>
    public bool IsBackfillFull
    {
        get => Backfill == EGroupBackfill.Full;
        set
        {
            if (value) Backfill = EGroupBackfill.Full;
        }
    }

    /// <summary>
    /// 收成请求
    /// </summary>
    /// <returns>加人请求</returns>
    public GroupAddRequest ToRequest() => new([..Picker.Picked], [..Picker.PickedModelNames], Backfill);
}

/// <summary>加人请求</summary>
/// <param name="Members">要加的角色，顺序即排进名单的顺序</param>
/// <param name="ModelNames">各自的模型名，顺序与 <paramref name="Members"/> 一致；null = 跟随全局</param>
/// <param name="Backfill">补历史的方式</param>
public sealed record GroupAddRequest(IReadOnlyList<CharacterData> Members, IReadOnlyList<string?> ModelNames,
    EGroupBackfill Backfill);
