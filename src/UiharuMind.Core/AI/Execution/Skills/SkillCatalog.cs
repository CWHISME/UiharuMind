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
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Execution.Skills;

/// <summary>
/// 技能目录条目:设置页与点名补全列表共用的稳定投影(框架类型止步于 Core 内部)
/// </summary>
public class SkillCatalogEntry
{
    /// <summary>技能名(规范要求等于目录名,否则框架拒绝加载)</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>描述(frontmatter description)</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>技能目录路径</summary>
    public string DirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// 相对技能根的目录路径,一律用 / 分隔(如 <c>pack/skills/engineering/tdd</c>)。
    /// 只给事实不给分组:怎么按它分组是展示决策,留在 UI 层。
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// 是否参与模型自选。false 表示 SKILL.md 里声明了
    /// <see cref="SkillCatalog.DisableModelInvocationKey"/>,该技能只剩点名调用可达。
    /// </summary>
    public bool IsModelInvocable { get; init; } = true;

    /// <summary>加载状态。设置页据此标记,免得用户看着开关是开的、实际从未生效</summary>
    public ESkillLoadState LoadState { get; init; } = ESkillLoadState.Loaded;

    /// <summary>
    /// 顶掉本技能的那个技能目录路径。仅 <see cref="ESkillLoadState.DuplicateName"/> 时有值,
    /// 用户要据此判断该删哪一个
    /// </summary>
    public string DuplicateOfPath { get; init; } = string.Empty;

    /// <summary>框架是否接受加载</summary>
    public bool IsLoaded => LoadState == ESkillLoadState.Loaded;
}

/// <summary>
/// 技能的加载状态。两种失败要给用户的动作完全不同(改 SKILL.md / 删掉一个包),
/// 因此不能合并成一个 bool
/// </summary>
public enum ESkillLoadState
{
    /// <summary>已加载</summary>
    Loaded,

    /// <summary>不符合 SKILL.md 规范校验被拒,最常见是技能名与目录名不一致,具体原因在日志里</summary>
    Invalid,

    /// <summary>技能名与另一个已加载的技能重复,被先到的顶掉</summary>
    DuplicateName,
}

/// <summary>
/// 一次点名调用的产物。见 docs/adr/0001:点名调用不走框架技能工具,
/// 正文直接注入本轮对话,因此对所有启用的技能一律可用。
/// </summary>
public sealed class SkillInvocation
{
    /// <summary>技能名</summary>
    public required string SkillName { get; init; }

    /// <summary>注入模型的文本(去 frontmatter 的正文 + 技能目录路径 + 用户参数)</summary>
    public required string InjectedText { get; init; }

    /// <summary>
    /// 解析 "/技能名 参数" 形式的点名调用
    /// </summary>
    /// <param name="text">用户输入的整行</param>
    /// <param name="skillName">技能名</param>
    /// <param name="arguments">参数,可为空串</param>
    /// <returns>是否是点名调用形式</returns>
    public static bool TryParse(string text, out string skillName, out string arguments)
    {
        skillName = string.Empty;
        arguments = string.Empty;
        if (text.Length < 2 || text[0] != '/') return false;

        int split = text.IndexOfAny([' ', '\t', '\n', '\r']);
        skillName = split < 0 ? text[1..] : text[1..split];
        arguments = split < 0 ? string.Empty : text[(split + 1)..].Trim();
        return skillName.Length > 0;
    }

    /// <summary>
    /// 判断输入是否正处于点名状态:整行以 / 开头且技能名还没写完(尚无空白)。
    /// 敲下空白即视为开始写参数,补全应当收起。
    /// </summary>
    /// <param name="text">输入框全文</param>
    /// <param name="prefix">已敲出的技能名前缀</param>
    /// <returns>是否应显示补全</returns>
    public static bool TryParsePrefix(string text, out string prefix)
    {
        prefix = string.Empty;
        if (text.Length == 0 || text[0] != '/') return false;

        string rest = text[1..];
        if (rest.Any(char.IsWhiteSpace)) return false;
        prefix = rest;
        return true;
    }
}

/// <summary>
/// 技能目录管理:遵循框架 SKILL.md 规范(agentskills.io)。
/// 技能有两条触发链路——<b>模型自选</b>由框架 AgentSkillsProvider 驱动(经
/// <see cref="BuildSkillsSource"/> 决定谁进广告列表),<b>点名调用</b>由
/// <see cref="TryBuildInvocationAsync"/> 组装并由宿主注入本轮对话。
/// </summary>
public class SkillCatalog : Singleton<SkillCatalog>
{
    /// <summary>
    /// frontmatter 键:声明本技能退出模型自选,只剩点名调用可达。
    /// <b>顶层与 <c>metadata:</c> 块两处都认</b>——生态(Claude Code 及其技能市场)一律写在顶层,
    /// 而框架解析器只认 name/description/license/compatibility/allowed-tools 与 metadata 块,
    /// 顶层未知键会被静默丢弃。只读 metadata 会让别处拿来的技能包悄悄失去这个声明。
    /// </summary>
    public const string DisableModelInvocationKey = "disable-model-invocation";

    private const string SkillFileName = "SKILL.md";

    private readonly SkillDirectoryScanner _scanner = new();
    private readonly Lock _sourceLock = new();
    private AgentSkillsSource? _sharedSource; //扫盘 + 解析的结果缓存,设置页「重新扫描」负责作废

    /// <summary>技能根目录</summary>
    public string SkillsRootPath => AppPaths.Data.Skills;

    /// <summary>
    /// 全进程共用的技能来源:<c>Caching(Deduplicating(DeepFile))</c>。
    ///
    /// 三层各司其职——<see cref="DeepFileSkillsSource"/> 扫出任意深度的技能目录并交框架解析;
    /// 去重层收拾多根扫描的重叠(不套它会拿到成对的重复项),顺带把重名技能按先到先得压成一个;
    /// 缓存层让设置页刷新、每次点名调用、每次建会话共用同一份扫描结果。
    ///
    /// 缓存隔离键钉成常量:默认按 agent 隔离,而本来源根本不看 <c>context</c>,
    /// 按 agent 分只会让同一份结果被反复扫。
    /// </summary>
    private AgentSkillsSource SharedSource
    {
        get
        {
            lock (_sourceLock)
            {
                return _sharedSource ??= new CachingAgentSkillsSource(
                    new DeduplicatingAgentSkillsSource(new DeepFileSkillsSource(SkillsRootPath)),
                    new CachingAgentSkillsSourceOptions { CacheIsolationKeySelector = _ => string.Empty });
            }
        }
    }

    /// <summary>
    /// 丢弃缓存的扫描结果,下次读取重新扫盘。设置页的「重新扫描」与「新建技能」用
    /// </summary>
    public void Invalidate()
    {
        lock (_sourceLock)
        {
            _sharedSource?.Dispose();
            _sharedSource = null;
        }
    }

    /// <summary>
    /// 构建供 HarnessAgent 使用的技能来源:只放"启用且参与模型自选"的技能。
    /// 被滤掉的技能框架 <c>load_skill</c> 同样找不到(广告与加载共用一份列表),
    /// 所以退出自选的技能只能经点名调用注入——这正是设计的支点,别改成不过滤。
    /// </summary>
    /// <param name="disabledSkills">本智能体禁用的技能名(角色自带,见 AgentToolConfig)</param>
    /// <returns>技能来源</returns>
    public AgentSkillsSource BuildSkillsSource(IEnumerable<string> disabledSkills)
    {
        HashSet<string> disabled = new(disabledSkills, StringComparer.OrdinalIgnoreCase);
        // 过滤层按角色一个,底下的扫描与解析共用 SharedSource——禁用清单是角色的,扫描结果不是
        return new FilteringAgentSkillsSource(SharedSource, (skill, _) => IsAdvertised(skill, disabled));
    }

    /// <summary>
    /// 技能是否进广告列表(即模型能否自选)。两个否决条件:用户禁用了它,
    /// 或它自己声明退出模型自选。
    /// </summary>
    /// <param name="skill">技能</param>
    /// <param name="disabledSkills">用户禁用的技能名(应为忽略大小写的集合)</param>
    /// <returns>是否进广告列表</returns>
    internal static bool IsAdvertised(AgentSkill skill, ICollection<string> disabledSkills)
    {
        return !disabledSkills.Contains(skill.Frontmatter.Name) && IsModelInvocable(skill);
    }

    /// <summary>
    /// 扫描技能目录,返回设置页展示列表。已加载的走框架解析(描述、校验、目录名一致性全由它管),
    /// 再补上"有 SKILL.md 却没被接受"的目录并标记未加载。
    /// </summary>
    /// <returns>技能条目列表</returns>
    public async Task<List<SkillCatalogEntry>> GetEntriesAsync()
    {
        List<SkillCatalogEntry> entries = new();
        Dictionary<string, string> loadedDirectories = new(StringComparer.Ordinal); //目录 -> 技能名
        Dictionary<string, string> loadedNames = new(StringComparer.OrdinalIgnoreCase); //技能名 -> 目录

        foreach (AgentSkill skill in await LoadSkillsAsync().ConfigureAwait(false))
        {
            string directory = NormalizePath((skill as AgentFileSkill)?.Path ?? string.Empty);
            if (directory.Length > 0)
            {
                loadedDirectories[directory] = skill.Frontmatter.Name;
                loadedNames.TryAdd(skill.Frontmatter.Name, directory);
            }

            entries.Add(new SkillCatalogEntry
            {
                Name = skill.Frontmatter.Name,
                Description = skill.Frontmatter.Description,
                DirectoryPath = directory,
                RelativePath = ToRelativePath(directory),
                IsModelInvocable = IsModelInvocable(skill),
            });
        }

        // 扫得到却没进上面那份列表的目录:要么规范校验没过,要么技能名撞了别人。
        // 两者给用户的动作不同(改 SKILL.md / 删掉一个包),所以要分开判
        foreach (SkillDirectory directory in await _scanner.ScanAsync(SkillsRootPath).ConfigureAwait(false))
        {
            string full = NormalizePath(directory.FullPath);
            if (loadedDirectories.ContainsKey(full)) continue;

            string name = ReadDeclaredName(full);
            bool duplicated = name.Length > 0 && loadedNames.ContainsKey(name);
            entries.Add(new SkillCatalogEntry
            {
                Name = name.Length > 0 ? name : Path.GetFileName(full),
                DirectoryPath = full,
                RelativePath = directory.RelativePath,
                LoadState = duplicated ? ESkillLoadState.DuplicateName : ESkillLoadState.Invalid,
                DuplicateOfPath = duplicated ? loadedNames[name] : string.Empty,
            });
        }

        return entries;
    }

    /// <summary>取一个技能目录自己声明的技能名;读不到为空串</summary>
    private static string ReadDeclaredName(string directory)
    {
        try
        {
            string file = Path.Combine(directory, SkillFileName);
            return File.Exists(file)
                ? ReadTopLevelFrontmatterValue(File.ReadAllText(file), "name") ?? string.Empty
                : string.Empty;
        }
        catch (Exception e)
        {
            Log.Warning($"Read skill name failed '{directory}': {e.Message}");
            return string.Empty;
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Length == 0 ? path : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>技能目录绝对路径 → 相对技能根的路径(/ 分隔);不在根下时退回目录名</summary>
    private string ToRelativePath(string directory)
    {
        if (directory.Length == 0) return string.Empty;

        string relative = Path.GetRelativePath(SkillsRootPath, directory).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)
            ? Path.GetFileName(directory)
            : relative;
    }

    /// <summary>
    /// 可点名调用的技能:已加载且未被用户禁用。是否参与模型自选不影响这里——
    /// 点名调用对两者一律开放。
    /// </summary>
    /// <param name="disabledSkills">本智能体禁用的技能名</param>
    /// <returns>技能条目列表</returns>
    public async Task<List<SkillCatalogEntry>> GetInvocableEntriesAsync(IEnumerable<string> disabledSkills)
    {
        HashSet<string> disabled = new(disabledSkills, StringComparer.OrdinalIgnoreCase);
        List<SkillCatalogEntry> entries = await GetEntriesAsync().ConfigureAwait(false);
        return entries.Where(x => x.IsLoaded && !disabled.Contains(x.Name)).ToList();
    }

    /// <summary>
    /// 组装一次点名调用。正文剔掉 frontmatter(name/description 只为进广告列表而存,
    /// 对模型是冗余 token),但保留框架在原文后追加的 available_resources / available_scripts 清单
    /// ——那正是模型需要的附件与脚本索引,且与 <c>load_skill</c> 的返回一致。
    ///
    /// 另附技能目录绝对路径,并明确要求走文件与 shell 工具:退出模型自选的技能不在框架 source
    /// 列表里,<c>read_skill_resource</c> / <c>run_skill_script</c> 对它一律返回 not found。
    /// </summary>
    /// <param name="skillName">技能名</param>
    /// <param name="arguments">技能名之后用户写的参数,可为空</param>
    /// <param name="tools">本智能体的能力配置(禁用清单与文件/shell 开关都从这里读)</param>
    /// <returns>调用产物;技能不存在或已被禁用时为 null</returns>
    public async Task<SkillInvocation?> TryBuildInvocationAsync(string skillName, string arguments,
        AgentToolConfig tools)
    {
        if (string.IsNullOrWhiteSpace(skillName)) return null;

        HashSet<string> disabled = new(tools.DisabledSkills, StringComparer.OrdinalIgnoreCase);
        if (disabled.Contains(skillName)) return null;

        foreach (AgentSkill skill in await LoadSkillsAsync().ConfigureAwait(false))
        {
            if (!string.Equals(skill.Frontmatter.Name, skillName, StringComparison.OrdinalIgnoreCase)) continue;

            string content = await skill.GetContentAsync().ConfigureAwait(false);
            string directory = (skill as AgentFileSkill)?.Path ?? string.Empty;

            StringBuilder sb = new();
            sb.AppendLine($"# Skill: {skill.Frontmatter.Name}");
            sb.AppendLine("The user invoked this skill explicitly. Follow its instructions for this task.");
            if (directory.Length > 0)
            {
                sb.AppendLine($"Skill directory: {directory}");
                sb.Append(BuildResourceAccessLine(tools.EnableFileAccess, tools.EnableShellExecution,
                    IsModelInvocable(skill)));
            }

            sb.AppendLine();
            sb.Append(StripFrontmatter(content));

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(arguments.Trim());
            }

            return new SkillInvocation
            {
                SkillName = skill.Frontmatter.Name,
                InjectedText = sb.ToString(),
            };
        }

        return null;
    }

    /// <summary>
    /// 交代技能的附件与脚本该怎么取。措辞<b>随实际启用的工具集裁剪</b>——
    /// 门控关掉的工具一个字都不提,否则就是在指挥一个不存在的工具
    /// (那正是被删掉的示范技能的毛病,不该在这里重犯一遍)。
    /// </summary>
    /// <param name="hasFileTools">文件工具是否启用</param>
    /// <param name="hasShell">shell 工具是否启用</param>
    /// <param name="isModelInvocable">技能是否仍参与模型自选</param>
    /// <returns>提示行(可能为空串)</returns>
    internal static string BuildResourceAccessLine(bool hasFileTools, bool hasShell, bool isModelInvocable)
    {
        StringBuilder sb = new();

        string? tools = (hasFileTools, hasShell) switch
        {
            (true, true) => "your file and shell tools",
            (true, false) => "your file tools",
            (false, true) => $"`{CharacterRunnerFactory.ShellToolName}`",
            _ => null, //两样都关:附件与脚本根本取不到,不如什么都不承诺
        };

        if (tools != null)
        {
            string what = hasShell ? "read its resources and run its scripts" : "read its resources";
            sb.AppendLine($"Use {tools} to {what} from that directory.");
        }

        // 退出模型自选的技能不在框架 source 列表里,技能工具对它一律返回 not found。
        // 仍参与自选的技能恰恰相反——那几个工具是好用的,不该拦着不让用
        if (!isModelInvocable)
        {
            sb.AppendLine("The skill tools cannot reach this skill — do not call them for it.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成一个技能模板目录,供设置页"新建技能"用。首次上手全靠它,
    /// 因此模板里要把易错点(名字必须等于目录名、描述即模型的匹配依据)写进注释。
    /// </summary>
    /// <returns>新建的技能目录路径;失败为 null</returns>
    public string? CreateSkillTemplate()
    {
        try
        {
            Directory.CreateDirectory(SkillsRootPath);

            string name = "new-skill";
            for (int i = 2; Directory.Exists(Path.Combine(SkillsRootPath, name)); i++) name = $"new-skill-{i}";

            string directory = Path.Combine(SkillsRootPath, name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, SkillFileName), BuildTemplate(name));
            Invalidate(); //刚落盘的技能要立刻出现在列表里
            return directory;
        }
        catch (Exception e)
        {
            Log.Warning($"Create skill template failed: {e.Message}");
            return null;
        }
    }

    private static string BuildTemplate(string name)
    {
        return $"""
                ---
                name: {name}
                description: 一句话说明什么时候该用这个技能。模型就是拿这句去匹配的,所以要点明触发场景。
                # name 必须与所在目录名完全一致,否则框架会直接拒绝加载这个技能。
                # 取消下面一行的注释,本技能即成为「主动技能」:模型不会自己用它,只能由你敲 /{name} 触发。
                # disable-model-invocation: true
                ---

                # {name}

                写清楚模型该按什么步骤做事。可以在本目录里放附件与脚本,
                点名调用时模型会拿到本目录的绝对路径,用文件工具与 shell 自行读取和执行。
                """;
    }

    // [MFA绕坑] 绕:枚举技能时给 GetSkillsAsync 传 null 上下文 因:AgentSkillsSourceContext 强制要求 agent 非空,而设置页与 / 补全都没有 agent;文件来源完全忽略该参数 删除条件:框架提供不需要 agent 的枚举入口
    private async Task<IList<AgentSkill>> LoadSkillsAsync()
    {
        try
        {
            return await SharedSource.GetSkillsAsync(null!).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warning($"Enumerate skills failed: {e.Message}");
            return [];
        }
    }

    /// <summary>
    /// 技能是否参与模型自选。见 <see cref="DisableModelInvocationKey"/>:
    /// 顶层与 <c>metadata:</c> 两处任一声明为 true 即退出。
    /// </summary>
    /// <param name="skill">技能</param>
    /// <returns>是否参与模型自选</returns>
    internal static bool IsModelInvocable(AgentSkill skill)
    {
        if (skill.Frontmatter.Metadata?.TryGetValue(DisableModelInvocationKey, out object? value) == true &&
            IsYamlTrue(value?.ToString()))
        {
            return false;
        }

        return !IsYamlTrue(ReadTopLevelFrontmatterValue(ReadRawContent(skill), DisableModelInvocationKey));
    }

    private static bool IsYamlTrue(string? value)
    {
        return string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 取技能原文。走 <c>GetContentAsync</c> 而非重新读盘:文件技能的原文已在内存,
    /// 该调用同步完成、零 IO,因此在同步的过滤谓词里也能用。
    /// </summary>
    /// <param name="skill">技能</param>
    /// <returns>原文;取不到时为空串</returns>
    private static string ReadRawContent(AgentSkill skill)
    {
        try
        {
            ValueTask<string> pending = skill.GetContentAsync();
            return pending.IsCompletedSuccessfully ? pending.Result : string.Empty; //非同步实现不在此阻塞
        }
        catch (Exception e)
        {
            Log.Warning($"Read skill content failed '{skill.Frontmatter.Name}': {e.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 从 SKILL.md 原文的 frontmatter 里读一个<b>顶层</b>键。
    /// 框架解析器把未知顶层键丢掉了(见 <see cref="DisableModelInvocationKey"/>),只能自己捞。
    /// </summary>
    /// <param name="content">SKILL.md 全文</param>
    /// <param name="key">顶层键名</param>
    /// <returns>键值(已去引号);无此键时为 null</returns>
    internal static string? ReadTopLevelFrontmatterValue(string content, string key)
    {
        if (!TrySplitFrontmatter(content, out string frontmatter, out _)) return null;

        foreach (string line in frontmatter.Split('\n'))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) continue; //缩进行属 metadata 等嵌套块

            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (!line.AsSpan(0, colon).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return line[(colon + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    /// <summary>
    /// 剔掉 SKILL.md 的 YAML frontmatter
    /// </summary>
    /// <param name="content">SKILL.md 全文</param>
    /// <returns>正文</returns>
    internal static string StripFrontmatter(string content)
    {
        TrySplitFrontmatter(content, out _, out string body);
        return body;
    }

    /// <summary>
    /// 切开 SKILL.md 的 frontmatter 与正文
    /// </summary>
    /// <param name="content">SKILL.md 全文</param>
    /// <param name="frontmatter">两道 --- 之间的内容(不含分隔行)</param>
    /// <param name="body">正文;无 frontmatter 时为全文</param>
    /// <returns>是否存在 frontmatter</returns>
    internal static bool TrySplitFrontmatter(string content, out string frontmatter, out string body)
    {
        frontmatter = string.Empty;
        body = content.Trim();

        string[] lines = content.Split('\n');
        if (lines.Length == 0 || lines[0].TrimStart('﻿').Trim() != "---") return false;

        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "---") continue;
            frontmatter = string.Join('\n', lines.Skip(1).Take(i - 1));
            body = string.Join('\n', lines.Skip(i + 1)).Trim();
            return true;
        }

        return false;
    }
}
