/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System;
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Skills;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 输入框的 <c>/</c> 命令面板：点名调用技能的补全候选与内置命令。
///
/// 采纳候选要改写输入框，因此持一个写回委托而不是反向持有对话视图模型——
/// 反向持有会让整套补全逻辑离不开一个真的 ConversationViewModel（原先的测试就得那么写）。
/// </summary>
public partial class CommandPaletteViewData : ObservableObject
{
    /// <summary>手动压缩命令</summary>
    public const string CompactCommand = "/compact";

    private readonly Action<string> _setInputText;
    private readonly Func<CharacterData> _character;

    private List<SkillCatalogEntry>? _skillCandidateCache; //一次点名期间复用,不每敲一个字读盘
    private int _skillPickerVersion;

    /// <summary>/ 补全的候选技能;敲空格进入参数后收起</summary>
    public ObservableCollection<SkillCatalogEntry> SkillCandidates { get; } = new();

    /// <summary>补全采纳后触发。本类不碰控件,由宿主把焦点与光标交还输入框末尾</summary>
    public event Action? SkillCandidateAccepted;

    [ObservableProperty] private bool _isSkillPickerOpen;
    [ObservableProperty] private int _skillCandidateIndex;

    /// <param name="setInputText">把输入框内容替换成给定文本</param>
    /// <param name="character">取当前会话的角色(尚无会话时是新建会话将使用的那个)</param>
    public CommandPaletteViewData(Action<string> setInputText, Func<CharacterData> character)
    {
        _setInputText = setInputText;
        _character = character;
    }

    /// <summary>
    /// 技能只在 agent 会话有意义(扮演档工具集为空,注入过去只会让模型去调不存在的工具);
    /// 内置命令则各档都有——压缩对角色扮演的长对话同样生效
    /// </summary>
    private bool IsAgentSession => _character().Kind.IsAgent();

    /// <summary>
    /// 上下移动候选选择(补全开着时由输入框按键驱动)
    /// </summary>
    /// <param name="delta">移动量,可为负</param>
    public void MoveSkillSelection(int delta)
    {
        if (!IsSkillPickerOpen || SkillCandidates.Count == 0) return;
        int count = SkillCandidates.Count;
        SkillCandidateIndex = (SkillCandidateIndex + delta % count + count) % count;
    }

    /// <summary>
    /// 采纳当前候选:把输入补成 "/技能名 ",随即进入写参数状态
    /// </summary>
    /// <returns>是否采纳了候选(未开启或无候选时为 false,调用方据此决定是否改走原本的行为)</returns>
    public bool AcceptSkillCandidate()
    {
        if (!IsSkillPickerOpen) return false;
        if (SkillCandidateIndex < 0 || SkillCandidateIndex >= SkillCandidates.Count) return false;

        _setInputText($"/{SkillCandidates[SkillCandidateIndex].Name} ");
        CloseSkillPicker();
        SkillCandidateAccepted?.Invoke();
        return true;
    }

    /// <summary>收起补全</summary>
    public void CloseSkillPicker()
    {
        _skillPickerVersion++;
        _skillCandidateCache = null;
        IsSkillPickerOpen = false;
        SkillCandidates.Clear();
    }

    /// <summary>
    /// 按输入内容刷新候选:仅在整行以 / 开头且技能名未写完时弹出
    /// </summary>
    /// <param name="value">输入框当前内容</param>
    public async Task RefreshSkillCandidatesAsync(string value)
    {
        if (!SkillInvocation.TryParsePrefix(value, out string prefix))
        {
            if (IsSkillPickerOpen) CloseSkillPicker();
            return;
        }

        int version = ++_skillPickerVersion;
        List<SkillCatalogEntry> all = [];
        if (IsAgentSession)
        {
            all = _skillCandidateCache ??
                  await SkillCatalog.Instance.GetInvocableEntriesAsync(_character().Tools.DisabledSkills);
            if (version != _skillPickerVersion) return; //读盘期间输入又变了,丢弃本次结果
            _skillCandidateCache = all;
        }

        SkillCandidates.Clear();
        // 内置命令排在最前:它们数量少且固定,混在技能里按字典序排会找不着
        foreach (SkillCatalogEntry command in BuiltInCommands
                     .Where(x => x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            SkillCandidates.Add(command);
        }

        foreach (SkillCatalogEntry entry in all
                     .Where(x => x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            SkillCandidates.Add(entry);
        }

        SkillCandidateIndex = 0;
        IsSkillPickerOpen = SkillCandidates.Count > 0;
    }

    /// <summary>
    /// 内置命令。借技能条目的形状进同一个补全列表——它们对用户是同一件事（敲 <c>/</c> 弹出来的东西），
    /// 为一条命令另开一套列表控件与键盘导航不值当。
    /// </summary>
    //每次现取而不是缓存成静态字段:描述要跟着语言切换走,而静态初始化只跑一次
    private static IReadOnlyList<SkillCatalogEntry> BuiltInCommands =>
    [
        new()
        {
            Name = CompactCommand[1..], //列表里存的是不带斜杠的名字
            Description = LocalizationManager.Instance.GetString("CompactCommandDescription"),
        },
    ];

    /// <summary>
    /// 组装点名调用。只在 agent 会话开放——技能正文多在指挥工具,
    /// 而角色扮演档工具集为空,注入过去只会让模型去调不存在的工具。
    /// </summary>
    /// <param name="text">用户输入的整行</param>
    /// <returns>调用产物;不是点名调用、或技能不存在/已禁用时为 null</returns>
    public async Task<SkillInvocation?> TryBuildSkillInvocationAsync(string text)
    {
        if (!IsAgentSession || !SkillInvocation.TryParse(text, out string skillName, out string arguments))
        {
            return null;
        }

        return await SkillCatalog.Instance.TryBuildInvocationAsync(skillName, arguments, _character().Tools);
    }
}
