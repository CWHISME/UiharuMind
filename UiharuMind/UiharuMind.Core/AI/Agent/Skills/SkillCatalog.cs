/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Agent.Skills;

/// <summary>
/// 技能目录条目(设置页展示用,轻量解析 SKILL.md frontmatter)
/// </summary>
public class SkillCatalogEntry
{
    /// <summary>技能名(目录名)</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>描述(frontmatter description)</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>技能目录路径</summary>
    public string DirectoryPath { get; init; } = string.Empty;
}

/// <summary>
/// 技能目录管理:遵循框架 SKILL.md 规范(agentskills.io)。
/// 技能加载与注入由框架 AgentSkillsProvider/AgentFileSkillsSource 负责,
/// 此处仅提供:根目录约定、示范技能落盘、设置页列表与启停(经过滤装饰器生效)。
/// </summary>
public class SkillCatalog : Singleton<SkillCatalog>
{
    /// <summary>技能根目录</summary>
    public string SkillsRootPath => Path.Combine(SettingConfig.SaveDataPath, "Skills/");

    /// <summary>
    /// 构建供 HarnessAgent 使用的技能来源(应用禁用列表过滤)
    /// </summary>
    /// <returns>技能来源</returns>
    public AgentSkillsSource BuildSkillsSource()
    {
        AgentFileSkillsSource fileSource = new(SkillsRootPath);
        HashSet<string> disabled = new(AgentSettingConfig.Current.DisabledSkills, StringComparer.OrdinalIgnoreCase);
        if (disabled.Count == 0) return fileSource;
        return new FilteringAgentSkillsSource(fileSource, (skill, _) => !disabled.Contains(skill.Frontmatter.Name));
    }

    /// <summary>
    /// 扫描技能目录,返回设置页展示列表
    /// </summary>
    /// <returns>技能条目列表</returns>
    public List<SkillCatalogEntry> GetEntries()
    {
        List<SkillCatalogEntry> entries = new();
        if (!Directory.Exists(SkillsRootPath)) return entries;

        foreach (string dir in Directory.GetDirectories(SkillsRootPath))
        {
            string skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile)) continue;
            entries.Add(new SkillCatalogEntry
            {
                Name = Path.GetFileName(dir),
                Description = ReadFrontmatterDescription(skillFile),
                DirectoryPath = dir,
            });
        }

        return entries;
    }

    /// <summary>
    /// 设置技能启停(持久化到 AgentSettingConfig.DisabledSkills)
    /// </summary>
    /// <param name="skillName">技能名</param>
    /// <param name="isEnabled">是否启用</param>
    public void SetSkillEnabled(string skillName, bool isEnabled)
    {
        List<string> disabled = AgentSettingConfig.Current.DisabledSkills;
        bool changed = isEnabled
            ? disabled.RemoveAll(x => string.Equals(x, skillName, StringComparison.OrdinalIgnoreCase)) > 0
            : disabled.All(x => !string.Equals(x, skillName, StringComparison.OrdinalIgnoreCase)) &&
              AddDisabled(disabled, skillName);
        if (changed) AgentSettingConfig.Current.Save();
    }

    /// <summary>
    /// 首次运行落盘示范技能(git 定时提交)
    /// </summary>
    public void EnsureDemoSkill()
    {
        string demoDir = Path.Combine(SkillsRootPath, "git-auto-commit");
        string skillFile = Path.Combine(demoDir, "SKILL.md");
        if (File.Exists(skillFile)) return;
        try
        {
            Directory.CreateDirectory(demoDir);
            File.WriteAllText(skillFile, """
                ---
                name: git-auto-commit
                description: Schedule automatic git commits, e.g. "commit this repo in 30 minutes".
                ---

                # git-auto-commit

                When the user asks to commit a git repository at a later time
                (e.g. "help me commit this project in half an hour"):

                1. Confirm the workspace is a git repository (run `git status` via the shell tool if unsure).
                2. Call the create_scheduled_task tool with:
                   - displayName: short description like "Auto git commit"
                   - delayMinutes: parsed from the user's request
                   - prompt: "Run git status to check for changes. If there are changes, stage them with
                     `git add -A` and commit with a concise message summarizing the diff
                     (`git commit -m \"...\"`). Do not push."
                   - preAuthorizedCommands: ["git status*", "git diff*", "git add*", "git commit*", "git log*"]
                3. Tell the user when the task will fire and that it only has git permissions.

                Never pre-authorize `git push` or any non-git command unless the user explicitly asks.
                """);
        }
        catch (Exception e)
        {
            Log.Warning($"Create demo skill failed: {e.Message}");
        }
    }

    private static bool AddDisabled(List<string> disabled, string skillName)
    {
        disabled.Add(skillName);
        return true;
    }

    private static string ReadFrontmatterDescription(string skillFile)
    {
        try
        {
            bool inFrontmatter = false;
            foreach (string line in File.ReadLines(skillFile))
            {
                string trimmed = line.Trim();
                if (trimmed == "---")
                {
                    if (inFrontmatter) break;
                    inFrontmatter = true;
                    continue;
                }

                if (inFrontmatter && trimmed.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed["description:".Length..].Trim().Trim('"');
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Read skill frontmatter failed '{skillFile}': {e.Message}");
        }

        return string.Empty;
    }
}
