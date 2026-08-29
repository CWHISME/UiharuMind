/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Skills;

/// <summary>
/// 扫到的一个技能目录
/// </summary>
/// <param name="FullPath">绝对路径</param>
/// <param name="RelativePath">相对技能根的路径,一律用 / 分隔(如 <c>pack/skills/engineering/tdd</c>)</param>
public sealed record SkillDirectory(string FullPath, string RelativePath);

/// <summary>
/// 找出技能根下所有含 SKILL.md 的目录。
///
/// 单独成类是因为它替换的正是框架做不了的那一件事:<c>AgentFileSkillsSource</c> 的
/// <c>MaxSkillDirectorySearchDepth</c> 是常量 2,<c>SearchDepth</c> 选项传再大也被 clamp,
/// 而生态里的技能包普遍是 <c>包名/skills/分类/技能名</c> 四层,一个都扫不到。
/// </summary>
public sealed class SkillDirectoryScanner
{
    private const string SkillFileName = "SKILL.md";

    /// <summary>不限深度:深度上限卡不进 glob 表达式(brace 内不许有 /),事后过滤又省不下扫描,徒增漏扫</summary>
    private const string SkillFilePattern = "**/" + SkillFileName;

    /// <summary>命中上限。技能几十个是常态,给到千级只为兜住"把整个代码仓库丢进技能目录"这种误操作</summary>
    private const int MaxResults = 1000;

    /// <summary>
    /// 扫描技能根
    /// </summary>
    /// <param name="rootPath">技能根目录</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>技能目录列表,按相对路径排序</returns>
    public async Task<List<SkillDirectory>> ScanAsync(string rootPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(rootPath)) return [];

        // 复用工具侧的 globber:node_modules/.git/bin/obj 的硬排除与目录剪枝都在里面,不另起一套
        GlobOutcome outcome = await new SimpleGlobber(rootPath)
            .SearchAsync(SkillFilePattern, maxResults: MaxResults, ct: ct).ConfigureAwait(false);

        if (outcome.Failure != null)
        {
            Log.Warning($"Scan skills failed '{rootPath}': {outcome.Failure.Kind}");
            return [];
        }

        if (outcome.Truncated)
        {
            Log.Warning($"Skill scan hit the {MaxResults} result cap, some skills are missing: {rootPath}");
        }

        List<string> directories = outcome.Entries
            .Select(x => ToDirectoryPath(x.Path))
            .Where(x => x.Length > 0) //技能根自己那份 SKILL.md:根不能当技能,否则父目录会被当成来源根扫到根的兄弟目录
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal) //祖先必然排在后代之前,下面的嵌套过滤依赖这个次序
            .ToList();

        return BuildOutermost(rootPath, directories);
    }

    /// <summary>
    /// 只留最外层的技能目录,与框架"遇到 SKILL.md 即停止下探"的规则对齐。
    /// 教人写技能的技能会把示例 SKILL.md 当附件放在自己目录里,不滤掉就会冒出一堆假技能。
    /// </summary>
    /// <param name="rootPath">技能根目录</param>
    /// <param name="sortedRelativePaths">已按序数排序的相对路径</param>
    /// <returns>技能目录列表</returns>
    private static List<SkillDirectory> BuildOutermost(string rootPath, List<string> sortedRelativePaths)
    {
        List<SkillDirectory> results = new(sortedRelativePaths.Count);
        List<string> accepted = new(sortedRelativePaths.Count);

        foreach (string relative in sortedRelativePaths)
        {
            if (accepted.Any(x => IsDescendantOf(relative, x))) continue;
            accepted.Add(relative);
            results.Add(new SkillDirectory(
                Path.GetFullPath(Path.Combine(rootPath, relative.Replace('/', Path.DirectorySeparatorChar))),
                relative));
        }

        return results;
    }

    private static bool IsDescendantOf(string path, string ancestor)
    {
        return path.Length > ancestor.Length &&
               path[ancestor.Length] == '/' &&
               path.StartsWith(ancestor, StringComparison.Ordinal);
    }

    /// <summary>SKILL.md 的相对路径 → 所在目录的相对路径;根下那份返回空串</summary>
    private static string ToDirectoryPath(string skillFileRelativePath)
    {
        int slash = skillFileRelativePath.LastIndexOf('/');
        return slash < 0 ? string.Empty : skillFileRelativePath[..slash];
    }
}
