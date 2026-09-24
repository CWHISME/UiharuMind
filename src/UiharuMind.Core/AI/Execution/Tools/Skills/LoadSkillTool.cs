/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Skills;

/// <summary>
/// 技能模型可见性总闸(ADR 0003 例外)关闭时的 <c>load_skill</c> 替代。
/// 总闸关掉后框架的 AgentSkillsProvider 整体不挂——广告列表与 load_skill 等
/// 三个工具一起从模型侧消失;但主动技能(点名注入)的正文经常引用被动技能
/// (如 implement 引用 tdd / code-review),没有同名工具模型就永远拿不到
/// 被引用技能的正文,链条在「知道了名字却加载不了」处断掉。
///
/// 语义刻意与框架 load_skill 对齐:闭包捕获过滤后的技能源(plan.SkillsSource),
/// 只加载「启用且参与模型自选」的技能——被角色禁用的、声明
/// <c>disable-model-invocation</c> 的主动技能一律 not found,守住
/// 「主动技能只能用户点名」的支点(ADR 0001 事实 1)。
/// 广告列表仍不注入,总闸省 token 的初衷不破:模型只加载它从点名注入的
/// 正文里得知的技能名。
///
/// 工具名与框架同名 <c>load_skill</c>:技能正文/生态里的引用原样可用,
/// 且自动命中 <see cref="ApprovalModeMapper"/> 的只读审批放行规则(按名匹配)。
/// </summary>
public static class LoadSkillTool
{
    /// <summary>工具名。与框架同名,见类注释</summary>
    public const string ToolName = "Skill";

    /// <summary>
    /// 创建 load_skill 替代工具
    /// </summary>
    /// <param name="source">技能源(取 plan.SkillsSource,已过滤到广告列表)</param>
    /// <param name="hasFileTools">文件工具是否挂载(资源读取提示按实际工具集裁剪)</param>
    /// <param name="hasShell">shell 工具是否挂载(同上)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(AgentSkillsSource source, bool hasFileTools, bool hasShell)
    {
        return AIFunctionFactory.Create(
            async (string skillName, CancellationToken cancellationToken = default) =>
                await LoadAsync(source, skillName, hasFileTools, hasShell, cancellationToken)
                    .ConfigureAwait(false),
            ToolName,
            "Loads the full content of a specific skill by name.");
    }

    private static async Task<string> LoadAsync(AgentSkillsSource source, string skillName,
        bool hasFileTools, bool hasShell, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(skillName)) return "Error: Skill name cannot be empty.";
        // null 源是装配错误;显式给出可读文案而不是让 NRE 的 message 漏进返回(测试钉住 unavailable)
        if (source == null) return "Error: Skill source is unavailable.";

        try
        {
            // 与 SkillCatalog.LoadSkillsAsync 同款:文件来源完全忽略 context,
            // 而这里拿到的 source 已被过滤到只剩广告列表(启用且参与模型自选)
            IList<AgentSkill> skills = await source.GetSkillsAsync(null!, cancellationToken)
                .ConfigureAwait(false);
            AgentSkill? skill = skills.FirstOrDefault(x =>
                string.Equals(x.Frontmatter.Name, skillName, StringComparison.OrdinalIgnoreCase));
            if (skill == null)
            {
                return $"Error: Skill '{skillName}' not found. Only model-selectable skills can be " +
                       $"loaded here; a skill declared disable-model-invocation must be triggered by " +
                       $"the user typing /{skillName}.";
            }

            string content = await skill.GetContentAsync(cancellationToken).ConfigureAwait(false);
            string directory = (skill as AgentFileSkill)?.Path ?? string.Empty;

            // 正文与框架 load_skill 一致;资源/脚本的取法走点名调用同款口径:
            // 框架的 read_skill_resource / run_skill_script 在总闸关闭时不挂,
            // 模型只能用文件与 shell 工具自取,故把目录与取法附在末尾
            StringBuilder sb = new();
            sb.Append(content.TrimEnd());
            if (directory.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine($"Skill directory: {directory}");
                sb.Append(SkillCatalog.BuildResourceAccessLine(hasFileTools, hasShell, isModelInvocable: true));
            }

            return sb.ToString();
        }
        catch (Exception e)
        {
            Log.Warning($"Load skill '{skillName}' failed: {e.Message}");
            return $"Error: Failed to load skill '{skillName}'. {e.Message}";
        }
    }
}
