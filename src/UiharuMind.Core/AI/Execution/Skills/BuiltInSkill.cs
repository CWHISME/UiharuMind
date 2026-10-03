using Microsoft.Agents.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Skills;

/// <summary>
/// 内置技能（ADR 0061）：随应用发布，正文由代码在<b>每次读取时</b>生成，不落盘。
/// 写死成文件的话，开发者模式运行中开关、命令行路径、步骤清单都会和应用脱节而没人察觉。
/// 没有技能目录：点名调用与 <c>load_skill</c> 都只拿正文，附件与脚本一概没有
/// </summary>
public sealed class BuiltInSkill : AgentSkill
{
    private readonly Func<string> _body;
    private readonly Func<bool> _isAvailable;

    /// <param name="name">技能名</param>
    /// <param name="description">描述：模型拿它匹配触发场景</param>
    /// <param name="body">生成正文（markdown，不带 frontmatter）。每次读取都调一次，取的是那一刻的事实</param>
    /// <param name="isAvailable">此刻是否可用；不可用时目录里没有它。省略为一直可用</param>
    public BuiltInSkill(string name, string description, Func<string> body, Func<bool>? isAvailable = null)
    {
        Frontmatter = new AgentSkillFrontmatter(name, description);
        _body = body;
        _isAvailable = isAvailable ?? (() => true);
    }

    /// <inheritdoc />
    public override AgentSkillFrontmatter Frontmatter { get; }

    /// <summary>此刻是否可用</summary>
    public bool IsAvailable => _isAvailable();

    /// <summary>
    /// 生成正文。同步完成：目录的过滤谓词要在同步上下文里读原文（见 <see cref="SkillCatalog"/>）
    /// </summary>
    /// <param name="cancellationToken">取消</param>
    /// <returns>正文</returns>
    public override ValueTask<string> GetContentAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_body());
}

/// <summary>
/// 文件技能之上叠内置技能：先文件、后内置，同名的内置让位——用户自己放一个同名技能就能顶掉它，
/// 被顶掉的内置在目录与设置列表里都不出现（即视为有意覆盖，不另提示）。
/// 套在缓存层外面：内置技能的可用与否（开发者模式）随时会变，不能跟扫盘结果一起缓存
/// </summary>
internal sealed class BuiltInSkillsSource : DelegatingAgentSkillsSource
{
    private readonly Func<IReadOnlyList<BuiltInSkill>> _builtIns;

    /// <param name="innerSource">文件技能来源</param>
    /// <param name="builtIns">取已登记的内置技能</param>
    public BuiltInSkillsSource(AgentSkillsSource innerSource, Func<IReadOnlyList<BuiltInSkill>> builtIns)
        : base(innerSource)
    {
        _builtIns = builtIns;
    }

    /// <inheritdoc />
    public override async Task<IList<AgentSkill>> GetSkillsAsync(AgentSkillsSourceContext context,
        CancellationToken cancellationToken = default)
    {
        IList<AgentSkill> files;
        try
        {
            files = await base.GetSkillsAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // 内层整体失败、无部分结果可保留：扫盘失败不连累内置，目录里至少还有它们
            Log.Warning($"Enumerate file skills failed, serving built-ins only: {e.GetType().Name}: {e.Message}");
            files = [];
        }
        HashSet<string> taken = new(files.Select(x => x.Frontmatter.Name), StringComparer.OrdinalIgnoreCase);
        List<AgentSkill> all = [..files];
        all.AddRange(_builtIns().Where(x => x.IsAvailable && !taken.Contains(x.Frontmatter.Name)));
        return all;
    }
}
