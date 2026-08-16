/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 项目级 MCP 配置的<b>安全授权账本</b>。
///
/// 为什么必须有：<c>.mcp.json</c> 是入库的，命令与参数由<b>仓库作者</b>控制。
/// 无条件自动读取 = 把工作区切到一个刚 clone 的仓库，就等于允许该仓库指定的任意子进程
/// 在本机启动。这是真实的供应链风险，没有商量余地。
///
/// <b>粒度是「工作区路径 + 每条 server 的可执行面指纹」</b>，两个维度都是必须的：
/// <list type="bullet">
/// <item>只按路径 → 仓库里改一行命令就能绕过已给的授权；</item>
/// <item>整份文件一个指纹 → 加第三个 server 会把已授权的两个一起重问，弹窗里是全量清单。
/// 看第三次的时候用户就不读了直接点确认，而<b>确认疲劳就是这类机制实际失效的方式</b>。
/// 逐条之后，弹窗永远只说「这一条是新的 / 这一条的命令被改成了 X」。</item>
/// </list>
///
/// <b>存本机配置目录，绝不进项目</b>：授权记录跟着被授权的文件一起入库就是自签名。
///
/// <b>全局作用域恒为已授权</b>：那份配置是用户自己在设置页写的，不存在"别人塞给你"的问题。
/// 授权拦的是入库分发这条路径，不是 MCP 本身。
/// </summary>
internal sealed class McpTrustStore
{
    private const string FileName = "McpTrust.json";

    private readonly object _lock = new();

    /// 键为规范化后的工作区绝对路径
    private Dictionary<string, List<McpTrustRecord>> _byWorkspace = new(StringComparer.Ordinal);

    /// <summary>
    /// 从磁盘读入，顺带<b>剔除已不存在的目录</b>。
    /// 照 <c>WorkspacePickerViewData.RefreshRecent</c> 那套现成做法，不另造清理机制：
    /// 留着一堆指向已删目录的授权，既是无用数据，也让"我到底授权过哪些项目"没法看。
    /// </summary>
    public void Reload()
    {
        Dictionary<string, List<McpTrustRecord>> loaded =
            SaveUtility.LoadRootFile<Dictionary<string, List<McpTrustRecord>>>(FileName)
            ?? new Dictionary<string, List<McpTrustRecord>>(StringComparer.Ordinal);

        Dictionary<string, List<McpTrustRecord>> alive = new(StringComparer.Ordinal);
        bool pruned = false;
        foreach ((string workspace, List<McpTrustRecord>? records) in loaded)
        {
            if (string.IsNullOrEmpty(workspace) || records == null) continue;
            if (!Directory.Exists(workspace))
            {
                pruned = true;
                continue;
            }

            alive[McpServerKey.NormalizeWorkspace(workspace)] = records;
        }

        lock (_lock)
        {
            _byWorkspace = alive;
        }

        if (pruned) Save();
    }

    /// <summary>
    /// 这条配置是否已获授权。全局作用域恒为 true。
    /// </summary>
    /// <param name="config">server 配置</param>
    /// <returns>已授权为 true</returns>
    public bool IsTrusted(McpServerConfig config)
    {
        if (!config.IsWorkspaceScoped) return true;

        string workspace = McpServerKey.NormalizeWorkspace(config.WorkspacePath);
        lock (_lock)
        {
            return _byWorkspace.TryGetValue(workspace, out List<McpTrustRecord>? records)
                   && Matches(records, config);
        }
    }

    /// <summary>
    /// 记下若干条授权并立即落盘。同名条目按新指纹覆盖——
    /// 「命令被改过、用户重新确认了」正是要覆盖旧记录，留着旧的等于永久放行那条已经不存在的命令。
    /// </summary>
    /// <param name="configs">用户已确认的配置（须为项目级；全局的忽略）</param>
    public void Approve(IEnumerable<McpServerConfig> configs)
    {
        bool changed = false;
        lock (_lock)
        {
            foreach (McpServerConfig config in configs)
            {
                if (!config.IsWorkspaceScoped) continue;

                string workspace = McpServerKey.NormalizeWorkspace(config.WorkspacePath);
                if (!_byWorkspace.TryGetValue(workspace, out List<McpTrustRecord>? records))
                {
                    records = new List<McpTrustRecord>();
                    _byWorkspace[workspace] = records;
                }

                records.RemoveAll(x => string.Equals(x.Name, config.Name, StringComparison.OrdinalIgnoreCase));
                records.Add(new McpTrustRecord
                {
                    Name = config.Name,
                    Fingerprint = McpServerFingerprint.Of(config),
                    ApprovedUtc = DateTime.UtcNow,
                });
                changed = true;
            }
        }

        if (changed) Save();
    }

    /// <summary>
    /// 撤销某工作区的全部授权（设置页的"改主意"入口）。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    public void Revoke(string workspacePath)
    {
        bool changed;
        lock (_lock)
        {
            changed = _byWorkspace.Remove(McpServerKey.NormalizeWorkspace(workspacePath));
        }

        if (changed) Save();
    }

    /// <summary>
    /// 已授权过的工作区清单（设置页展示用）
    /// </summary>
    /// <returns>规范化后的工作区路径</returns>
    public List<string> GetTrustedWorkspaces()
    {
        lock (_lock)
        {
            return _byWorkspace.Keys.ToList();
        }
    }

    /// <summary>
    /// 匹配规则：同名且<b>指纹相同</b>。抽成静态纯函数是为了可单测——
    /// 「改了命令还算已授权」这种缺陷在真机上极难发现，而它一旦发生，整套确认就形同虚设。
    /// </summary>
    /// <param name="records">该工作区的授权记录</param>
    /// <param name="config">待判定的配置</param>
    /// <returns>已授权为 true</returns>
    public static bool Matches(IReadOnlyList<McpTrustRecord> records, McpServerConfig config)
    {
        string fingerprint = McpServerFingerprint.Of(config);
        foreach (McpTrustRecord record in records)
        {
            if (!string.Equals(record.Name, config.Name, StringComparison.OrdinalIgnoreCase)) continue;
            return string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// 这条配置之前是否授权过（名字对得上但指纹变了）。
    /// 弹窗据此把「新增的一条」与「命令被改过的一条」分开说——后者更值得警惕。
    /// </summary>
    /// <param name="config">server 配置</param>
    /// <returns>授权过但已变更为 true</returns>
    public bool WasApprovedWithDifferentCommand(McpServerConfig config)
    {
        if (!config.IsWorkspaceScoped) return false;

        string workspace = McpServerKey.NormalizeWorkspace(config.WorkspacePath);
        lock (_lock)
        {
            return _byWorkspace.TryGetValue(workspace, out List<McpTrustRecord>? records)
                   && records.Any(x =>
                       string.Equals(x.Name, config.Name, StringComparison.OrdinalIgnoreCase))
                   && !Matches(records, config);
        }
    }

    private void Save()
    {
        Dictionary<string, List<McpTrustRecord>> snapshot;
        lock (_lock)
        {
            snapshot = _byWorkspace.ToDictionary(x => x.Key, x => new List<McpTrustRecord>(x.Value),
                StringComparer.Ordinal);
        }

        try
        {
            SaveUtility.SaveRootFile(FileName, snapshot);
        }
        catch (Exception e)
        {
            // 落盘失败只影响"下次还要再确认一遍",不该让当前这一轮起不来
            Log.Warning($"Save MCP trust records failed: {e.Message}");
        }
    }
}

/// <summary>
/// 一条授权记录。<b>不存命令原文</b>——那会在配置目录里留一份仓库内容的副本，
/// 而判定只需要指纹；要看命令去读那个 <c>.mcp.json</c>。
/// </summary>
public sealed class McpTrustRecord
{
    /// <summary>server 名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>获授权时的可执行面指纹（见 <c>McpServerFingerprint</c>）</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>授权时间（UTC）</summary>
    public DateTime ApprovedUtc { get; set; }
}
