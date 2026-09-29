/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 项目级 MCP server 的<b>送达方式</b>账本：(工作区, 名字) → 直挂 / 按需。
///
/// 为什么单立一个存储：项目级配置是入库共享的 <c>.mcp.json</c>，标准形状里没有这一项，
/// 而送达方式是这台机器、这个模型的偏好，不该替仓库里的其他人做决定。
/// 与 <see cref="McpTrustStore"/> 同理，存本机配置目录，绝不进项目。
///
/// <b>只记与默认值不同的</b>：默认是按需，账本里只会出现被改成直挂的那几条，
/// 改回默认即删除记录——账本不会因为翻来覆去而膨胀，也没有"记了个默认值"的歧义。
/// </summary>
internal sealed class McpWorkspaceMountStore
{
    private readonly object _lock = new();
    private readonly string _filePath;

    /// 键为规范化后的工作区绝对路径，再按 server 名（不区分大小写）索引
    private Dictionary<string, Dictionary<string, EMcpMountMode>> _byWorkspace = new(StringComparer.Ordinal);

    /// <param name="filePath">账本文件；缺省用应用的配置位置，单测可注入</param>
    public McpWorkspaceMountStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.Config.McpWorkspaceMounts;
    }

    /// <summary>从磁盘读入。读坏按空账本处理，不阻塞启动</summary>
    public void Reload()
    {
        Dictionary<string, Dictionary<string, EMcpMountMode>> loaded =
            SaveUtility.Load<Dictionary<string, Dictionary<string, EMcpMountMode>>>(_filePath) ?? new();

        Dictionary<string, Dictionary<string, EMcpMountMode>> normalized = new(StringComparer.Ordinal);
        foreach ((string workspace, Dictionary<string, EMcpMountMode>? modes) in loaded)
        {
            if (string.IsNullOrEmpty(workspace) || modes == null) continue;
            normalized[McpServerKey.NormalizeWorkspace(workspace)] =
                new Dictionary<string, EMcpMountMode>(modes, StringComparer.OrdinalIgnoreCase);
        }

        lock (_lock)
        {
            _byWorkspace = normalized;
        }
    }

    /// <summary>
    /// 取某个项目级 server 的送达方式。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    /// <param name="serverName">server 名</param>
    /// <returns>账本里的值；没记录即默认的按需</returns>
    public EMcpMountMode Get(string? workspacePath, string serverName)
    {
        string workspace = McpServerKey.NormalizeWorkspace(workspacePath);
        lock (_lock)
        {
            return _byWorkspace.TryGetValue(workspace, out Dictionary<string, EMcpMountMode>? modes)
                   && modes.TryGetValue(serverName, out EMcpMountMode mode)
                ? mode
                : EMcpMountMode.OnDemand;
        }
    }

    /// <summary>
    /// 改某个项目级 server 的送达方式并立即落盘。
    /// </summary>
    /// <param name="workspacePath">工作区路径</param>
    /// <param name="serverName">server 名</param>
    /// <param name="mode">新的送达方式；等于默认值时删除记录</param>
    /// <returns>是否真的变了</returns>
    public bool Set(string workspacePath, string serverName, EMcpMountMode mode)
    {
        string workspace = McpServerKey.NormalizeWorkspace(workspacePath);
        lock (_lock)
        {
            if (Get(workspace, serverName) == mode) return false;

            if (!_byWorkspace.TryGetValue(workspace, out Dictionary<string, EMcpMountMode>? modes))
            {
                modes = new Dictionary<string, EMcpMountMode>(StringComparer.OrdinalIgnoreCase);
                _byWorkspace[workspace] = modes;
            }

            if (mode == EMcpMountMode.OnDemand) modes.Remove(serverName);
            else modes[serverName] = mode;
            if (modes.Count == 0) _byWorkspace.Remove(workspace);
        }

        Save();
        return true;
    }

    private void Save()
    {
        Dictionary<string, Dictionary<string, EMcpMountMode>> snapshot;
        lock (_lock)
        {
            snapshot = _byWorkspace.ToDictionary(x => x.Key,
                x => new Dictionary<string, EMcpMountMode>(x.Value), StringComparer.Ordinal);
        }

        SaveUtility.Save(_filePath, snapshot);
    }
}
