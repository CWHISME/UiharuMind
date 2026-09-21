/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;

namespace UiharuMind.Core.AI.Execution.Skills;

/// <summary>
/// 任意深度的文件技能来源:自己扫出技能目录,再把它们的<b>父目录</b>交给框架的
/// <c>AgentFileSkillsSource</c> 多根重载去解析。
///
/// 只替换"找目录"这一件框架做不了的事(它的搜索深度是常量 2,见 <see cref="SkillDirectoryScanner"/>),
/// SKILL.md 解析、技能名与目录名一致性校验、available_resources 清单、资源路径逃逸防护
/// 全部仍由框架承担——自己重写一遍必然与框架行为分叉。
///
/// 父目录各扫 2 层必然互相重叠,因此<b>必须</b>套一层去重,见
/// <see cref="SkillCatalog.BuildSkillsSource"/>。
/// </summary>
internal sealed class DeepFileSkillsSource : AgentSkillsSource
{
    private readonly string _rootPath;
    private readonly SkillDirectoryScanner _scanner = new();

    /// <param name="rootPath">技能根目录</param>
    public DeepFileSkillsSource(string rootPath)
    {
        _rootPath = rootPath;
    }

    /// <inheritdoc />
    public override async Task<IList<AgentSkill>> GetSkillsAsync(AgentSkillsSourceContext context,
        CancellationToken cancellationToken = default)
    {
        List<SkillDirectory> directories = await _scanner.ScanAsync(_rootPath, cancellationToken)
            .ConfigureAwait(false);
        if (directories.Count == 0) return [];

        List<string> parents = directories
            .Select(x => Path.GetDirectoryName(x.FullPath) ?? _rootPath)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        using AgentFileSkillsSource source = new(parents);
        return await source.GetSkillsAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
