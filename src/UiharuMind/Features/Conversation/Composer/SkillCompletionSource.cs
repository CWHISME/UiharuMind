using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// <c>/</c> 补全：整行以 / 开头且技能名未写完时，给内置命令与可点名的技能。采纳即整行换成「/名字 」
/// </summary>
public sealed class SkillCompletionSource : ICompletionSource
{
    private readonly Func<CharacterData> _character;
    private readonly Func<bool> _isAgentSession;
    private readonly Func<IEnumerable<MentionTarget>>? _groupMembers; //成员非空即群壳:群里的 / 是死路
    private List<SkillCatalogEntry>? _cache; //一次点名期间复用,不每敲一个字读盘

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="character">取当前会话的角色（读它禁用了哪些技能）</param>
    /// <param name="isAgentSession">当前会话是否 agent 形态（chat 形态没有技能，只剩内置命令）</param>
    /// <param name="groupMembers">当前群的成员；有成员即群壳，整个源不匹配（群里 / 全是死候选）</param>
    public SkillCompletionSource(Func<CharacterData> character, Func<bool> isAgentSession,
        Func<IEnumerable<MentionTarget>>? groupMembers = null)
    {
        _character = character;
        _isAgentSession = isAgentSession;
        _groupMembers = groupMembers;
    }

    public bool TryMatch(string text, int caret, out CompletionMatch match)
    {
        // 与光标无关：点名只认整行，采纳也换整行
        match = default;
        if (_groupMembers?.Invoke().Any() == true) return false; //群壳的 / 是死路,整条不弹
        if (!SkillInvocation.TryParsePrefix(text, out string prefix)) return false;

        match = new CompletionMatch(0, text.Length, prefix);
        return true;
    }

    public async Task<IReadOnlyList<CompletionCandidate>> GetCandidatesAsync(string query)
    {
        List<SkillCatalogEntry> skills = [];
        if (_isAgentSession())
        {
            skills = _cache ??= await SkillCatalog.Instance.GetInvocableEntriesAsync(_character().Tools.DisabledSkills);
        }

        // 内置命令排在最前:它们数量少且固定,混在技能里按字典序排会找不着
        return CommandPaletteViewData.BuiltInCommands
            .Where(x => x.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Concat(skills
                .Where(x => x.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name, StringComparer.Ordinal))
            .Select(ToCandidate)
            .ToList();
    }

    public void Reset() => _cache = null;

    /// <summary>技能条目 → 补全候选（只能点名、不参与模型自选的挂一个标签）</summary>
    /// <param name="entry">技能条目</param>
    /// <returns>候选</returns>
    public static CompletionCandidate ToCandidate(SkillCatalogEntry entry) => new()
    {
        Label = "/" + entry.Name,
        Insertion = $"/{entry.Name} ",
        Description = entry.Description,
        Tag = entry.IsModelInvocable ? null : Loc.Text(LangKey.AgentSkillsUserInvoked),
        TagTip = Loc.Text(LangKey.AgentSkillsUserInvokedTip),
    };
}
