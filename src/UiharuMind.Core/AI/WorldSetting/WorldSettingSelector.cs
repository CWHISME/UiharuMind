using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.WorldSettings;

/// <summary>
/// 本轮该注入的世界设定选择结果：按位置分组、已按 Order 排序、已受预算约束。
/// </summary>
public sealed record WorldSettingSelection(
    IReadOnlyList<WorldSettingEntry> BeforeCharacter,
    IReadOnlyList<WorldSettingEntry> AfterCharacter)
{
    public bool IsEmpty => BeforeCharacter.Count == 0 && AfterCharacter.Count == 0;
}

/// <summary>
/// 世界设定的<b>纯选择逻辑</b>：从条目里挑出本轮该注入的，按位置分组、受 token 预算约束。
/// 常驻条目无条件注入；其余条目在扫描文本里命中任一关键词才注入。
/// 只做输入输出的纯函数，不碰单例——provider 只负责把数据喂进来，可单测。
/// </summary>
public static class WorldSettingSelector
{
    /// <summary>一次注入的 token 预算默认值，与知识库检索同一口径（MemorySearcher.DefaultTokenBudget）。</summary>
    public const int DefaultTokenBudget = 1500;

    /// <summary>条目数封顶。预算之外再加一道，避免碎条目过多时塞进几十条互相重复的短块。</summary>
    public const int MaxEntryCount = 32;

    /// <summary>
    /// 选择本轮注入的条目。
    /// </summary>
    /// <param name="setting">世界设定；null 或不含条目时返回空结果</param>
    /// <param name="scanText">扫描文本（最近几轮消息拼的），关键词在此内命中；常驻条目不看它</param>
    /// <returns>按位置分组的选中条目</returns>
    public static WorldSettingSelection Select(WorldSetting? setting, string scanText)
    {
        if (setting is null || setting.Entries.Count == 0) return new WorldSettingSelection([], []);

        // 激活：常驻，或任一关键词命中（大小写不敏感；空关键词不命中）。
        // 按位置分组后组内再按 Order —— 排序键是 (Position, Order)，一次排完。
        List<WorldSettingEntry> active = setting.Entries
            .Where(entry => entry.Constant || (entry.Keys.Count > 0 && Hits(entry.Keys, scanText)))
            .OrderBy(entry => entry.Position)
            .ThenBy(entry => entry.Order)
            .ToList();
        if (active.Count == 0) return new WorldSettingSelection([], []);

        int budget = setting.TokenBudget > 0 ? setting.TokenBudget : DefaultTokenBudget;
        List<WorldSettingEntry> before = [];
        List<WorldSettingEntry> after = [];
        int usedTokens = 0;
        foreach (WorldSettingEntry entry in active)
        {
            IList<WorldSettingEntry> group =
                entry.Position == EWorldSettingPosition.AfterCharacter ? after : before;

            // 全量第一条无论多大都收（与知识库检索 MemorySearcher 同款口径）：
            // 预算比单条还小的时候，交一条总比交空手好。只豁免第一条，
            // 前后两组不会各自豁免一条把预算打穿到两条最大条目之和
            if (before.Count + after.Count > 0 &&
                usedTokens + LlmTokenizer.CountTokens(entry.Content) > budget) break;
            if (before.Count + after.Count >= MaxEntryCount) break;

            group.Add(entry);
            usedTokens += LlmTokenizer.CountTokens(entry.Content);
        }

        return new WorldSettingSelection(before, after);
    }

    /// <summary>任一关键词在扫描文本里出现（大小写不敏感）。</summary>
    private static bool Hits(List<string> keys, string scanText)
    {
        if (scanText.Length == 0) return false;
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i].Trim();
            if (key.Length == 0) continue;
            if (scanText.Contains(key, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// 拼成注入块的正文（不含块头；块头由 provider 负责）。按位置分组、小节带标题，
    /// 条目间用与知识库片段同款的 <c>***</c> 分隔。
    /// </summary>
    public static string Format(WorldSettingSelection selection)
    {
        if (selection.IsEmpty) return "";

        StringBuilder sb = StringBuilderPool.Get();
        try
        {
            AppendGroup(sb, "Before the character definition:", selection.BeforeCharacter);
            AppendGroup(sb, "After the character definition:", selection.AfterCharacter);
            return sb.ToString().TrimEnd();
        }
        finally
        {
            StringBuilderPool.Release(sb);
        }
    }

    private static void AppendGroup(StringBuilder sb, string header, IReadOnlyList<WorldSettingEntry> entries)
    {
        if (entries.Count == 0) return;

        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine(header);
        for (int i = 0; i < entries.Count; i++)
        {
            sb.AppendLine(entries[i].Content.Trim());
            if (i < entries.Count - 1) sb.AppendLine("\n***");
        }
    }
}