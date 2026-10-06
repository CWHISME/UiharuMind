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
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Skills;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// 输入框的补全面板：<c>/</c> 点名调用技能与内置命令、群里的 <c>@</c> 成员。
/// 每种补全是一个 <see cref="ICompletionSource"/>，列表、键盘导航与写回在这里统一做。
///
/// 采纳候选要改写输入框，因此持一个写回委托而不是反向持有对话视图模型——
/// 反向持有会让整套补全逻辑离不开一个真的 ConversationViewModel（原先的测试就得那么写）。
/// </summary>
public partial class CommandPaletteViewData : ObservableObject
{
    /// <summary>手动压缩命令</summary>
    public const string CompactCommand = "/compact";

    /// <summary>
    /// 解析压缩命令的输入。整行匹配 <see cref="CompactCommand"/>（忽略大小写与首尾空白）；
    /// 命令后跟的文字作为用户附加的额外指示取出——它会随写交接文档的请求一起交给模型。
    /// </summary>
    /// <param name="text">用户敲的整行</param>
    /// <param name="extraInstructions">命令后跟的文字；没有时为 null</param>
    /// <returns>是不是压缩命令（即使带了参数也算）</returns>
    public static bool TryParseCompact(string text, out string? extraInstructions)
    {
        extraInstructions = null;
        string trimmed = text.Trim();
        if (trimmed.Length < CompactCommand.Length
            || !trimmed.StartsWith(CompactCommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length == CompactCommand.Length) return true;
        // 命令后紧跟非空白(如 /compactX)不算压缩命令,防止吞掉想聊这个单词的普通消息
        if (!char.IsWhiteSpace(trimmed[CompactCommand.Length])) return false;

        string rest = trimmed[CompactCommand.Length..].Trim();
        extraInstructions = rest.Length == 0 ? null : rest;
        return true;
    }

    private readonly Action<string, int> _setInputText;
    private readonly Func<CharacterData> _character;
    private readonly Func<bool> _isAgentSession;
    private readonly IReadOnlyList<ICompletionSource> _sources; //按顺序试，先认出的那一种生效
    private string _text = string.Empty; //最近一次刷新时的输入与匹配：采纳按它们改写
    private CompletionMatch _match;
    private int _version;
    private string? _dismissed; //Esc 收起时的输入：文字没变之前光标怎么挪都不再弹

    /// <summary>当前补全的候选（<c>/</c> 技能与内置命令，或群里的 <c>@</c> 成员）</summary>
    public ObservableCollection<CompletionCandidate> Candidates { get; } = new();

    /// <summary>补全采纳后触发，参数是采纳后光标该落的位置。本类不碰控件，由宿主把焦点与光标交还输入框</summary>
    public event Action<int>? CandidateAccepted;

    [ObservableProperty] private bool _isPickerOpen;
    [ObservableProperty] private int _candidateIndex;

    /// <param name="setInputText">把输入框内容替换成给定文本，并告知光标该落在哪（先记光标再写文本，免得写文本触发的刷新拿旧光标又弹出来）</param>
    /// <param name="character">取当前会话的角色(尚无会话时是新建会话将使用的那个)</param>
    /// <param name="isAgentSession">当前会话是否 agent 形态（ADR 0050：chat 形态即使挂着 agent 卡也没有技能）</param>
    /// <param name="groupMembers">当前群的成员（<c>@</c> 补全用）；不是群为空，省略即不开 @ 补全。
    /// 成员非空同时就是「这是群壳」的判据——建群保证至少一名成员，非群恒空——
    /// 群壳的发送路径在技能/命令解析之前就分流成群发言，敲 / 弹出来的技能与内置命令全是死候选，
    /// 所以 / 补全(技能+内置命令)只在成员为空时挂</param>
    public CommandPaletteViewData(Action<string, int> setInputText, Func<CharacterData> character,
        Func<bool> isAgentSession, Func<IEnumerable<MentionTarget>>? groupMembers = null)
    {
        _setInputText = setInputText;
        _character = character;
        _isAgentSession = isAgentSession;
        // / 补全(技能+内置命令)由 SkillCompletionSource 自己认群壳:见它的 TryMatch。
        // 判断必须惰性——群壳与否随会话切换变,不能在这里定死一次
        List<ICompletionSource> sources = [new SkillCompletionSource(character, isAgentSession, groupMembers)];
        if (groupMembers != null) sources.Add(new MentionCompletionSource(groupMembers));
        _sources = sources;
    }

    /// <summary>
    /// 技能只在 agent 会话有意义(扮演档工具集为空,注入过去只会让模型去调不存在的工具);
    /// 内置命令则各档都有——压缩对角色扮演的长对话同样生效
    /// </summary>
    private bool IsAgentSession => _isAgentSession();

    /// <summary>
    /// 上下移动候选选择(补全开着时由输入框按键驱动)
    /// </summary>
    /// <param name="delta">移动量,可为负</param>
    public void MoveSelection(int delta)
    {
        if (!IsPickerOpen || Candidates.Count == 0) return;
        int count = Candidates.Count;
        CandidateIndex = (CandidateIndex + delta % count + count) % count;
    }

    /// <summary>
    /// 采纳当前候选：把匹配到的那一段换成候选的写回文字（技能换整行，@ 只换光标前那半个名字）
    /// </summary>
    /// <returns>是否采纳了候选(未开启或无候选时为 false,调用方据此决定是否改走原本的行为)</returns>
    public bool AcceptCandidate()
    {
        if (!IsPickerOpen) return false;
        if (CandidateIndex < 0 || CandidateIndex >= Candidates.Count) return false;

        string insertion = Candidates[CandidateIndex].Insertion;
        int start = Math.Clamp(_match.Start, 0, _text.Length);
        int end = Math.Clamp(_match.End, start, _text.Length);
        string text = _text[..start] + insertion + _text[end..];
        int caret = start + insertion.Length;
        ClosePicker();
        _setInputText(text, caret);
        CandidateAccepted?.Invoke(caret);
        return true;
    }

    /// <summary>收起补全</summary>
    public void ClosePicker()
    {
        _version++;
        foreach (ICompletionSource source in _sources) source.Reset();
        IsPickerOpen = false;
        Candidates.Clear();
    }

    /// <summary>用户按 Esc 收起：同一段输入里不再自动弹出，直到文字变了</summary>
    public void Dismiss()
    {
        _dismissed = _text;
        ClosePicker();
    }

    /// <summary>
    /// 按输入与光标刷新候选：第一种认得出的补全生效，都认不出就收起
    /// </summary>
    /// <param name="value">输入框当前内容</param>
    /// <param name="caret">光标位置；不知道时传内容长度（视为在末尾）</param>
    public async Task RefreshAsync(string value, int caret)
    {
        if (_dismissed != null)
        {
            if (value == _dismissed) return;
            _dismissed = null;
        }

        ICompletionSource? source = null;
        CompletionMatch match = default;
        foreach (ICompletionSource candidate in _sources)
        {
            if (!candidate.TryMatch(value, caret, out match)) continue;
            source = candidate;
            break;
        }

        if (source == null)
        {
            if (IsPickerOpen) ClosePicker();
            return;
        }

        int version = ++_version;
        IReadOnlyList<CompletionCandidate> candidates = await source.GetCandidatesAsync(match.Query);
        if (version != _version) return; //读盘期间输入又变了,丢弃本次结果

        _text = value;
        _match = match;
        Candidates.Clear();
        foreach (CompletionCandidate candidate in candidates) Candidates.Add(candidate);
        CandidateIndex = 0;
        IsPickerOpen = Candidates.Count > 0;
    }

    /// <summary>
    /// 内置命令。借技能条目的形状进同一个补全列表——它们对用户是同一件事（敲 <c>/</c> 弹出来的东西），
    /// 为一条命令另开一套列表控件与键盘导航不值当。
    /// 内置命令排在同名技能前面：<c>SendCoreAsync</c> 先试命令再试技能点名，
    /// 因此同名技能会被内置命令遮蔽，不会同时出现两条路。
    /// </summary>
    //每次现取而不是缓存成静态字段:描述要跟着语言切换走,而静态初始化只跑一次
    internal static IReadOnlyList<SkillCatalogEntry> BuiltInCommands =>
    [
        new()
        {
            Name = CompactCommand[1..], //列表里存的是不带斜杠的名字
            Description = Loc.Text(LangKey.CompactCommandDescription),
        },
    ];

    /// <summary>
    /// 组装点名调用。只在 agent 会话开放——技能正文多在指挥工具,
    /// 而普通角色工具集为空,注入过去只会让模型去调不存在的工具。
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
