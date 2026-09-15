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

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// agent 产物（跑 Python 画的图、导出的数据、临时脚本、下载物）的磁盘布局：
/// <b>一个工作区一个家，家里按会话分房间</b>，根目录是 <c>Data/Agent/Workspaces/</c>。
/// 没绑工作区的会话统一进 <see cref="NoWorkspaceFolder"/> 桶（ADR 0026）。
///
/// 分房间是<b>正确性问题而非整洁问题</b>：产出以绝对路径写进对话历史，让不同会话
/// 共用一个目录时，两个会话各画一张 <c>chart.png</c>，后者会静默盖掉前者——
/// 旧对话回头看一切正常，只是图是错的。
///
/// 房间名只认会话 id 前 8 位、<b>不带标题</b>：改标题既不搬目录也不换目录
/// （旧布局 <c>{标题}_{id8}</c> 一改名就多一个目录）。工作区变了才会换房间，
/// 旧房间留着——历史链接仍有效。
/// </summary>
public static class AgentOutputLayout
{
    /// <summary>目录名里会话 id 的保留长度</summary>
    private const int IdLength = 8;

    /// <summary>没绑工作区的会话统一进的桶（避开 <c>Cache/Scratch</c>：那是可随手删的缓存，性质相反）</summary>
    public const string NoWorkspaceFolder = "NoWorkspace";

    /// <summary>所有会话的产出房间的根目录</summary>
    public static string RootPath => AppPaths.Data.AgentWorkspaces;

    /// <summary>
    /// 会话的产出目录名（相对 <see cref="RootPath"/>）：<c>工作区段/id8</c> 或 <c>NoWorkspace/id8</c>。
    /// </summary>
    /// <param name="workspacePath">会话绑定的工作目录；未绑定为 null 或空串</param>
    /// <param name="sessionId">会话标识</param>
    /// <returns>目录名；无会话时为空串（能力预览退回根）</returns>
    public static string GetFolderName(string? workspacePath, string sessionId)
    {
        // id 截到 8 位:会话 id 是 guid("N"),前 8 位撞车的概率在个人应用的量级上可以忽略。
        // FileMemoryLayout 那边不截是因为角色 id 是枚举名,Assistant/AssistantExpert 共前缀,一截就撞
        string id = Sanitize(sessionId, IdLength);
        if (id.Length == 0) return string.Empty;

        return string.IsNullOrWhiteSpace(workspacePath)
            ? $"{NoWorkspaceFolder}/{id}"
            : $"{WorkspaceSegment.From(workspacePath)}/{id}";
    }

    /// <summary>
    /// 会话房间的绝对路径。审批把"自己那一间"视为界内（免审批），注意只认这一间、
    /// 不认整棵 <see cref="RootPath"/>——跨会话覆盖仍要问
    /// </summary>
    /// <param name="folderName">房间目录名（<see cref="GetFolderName"/> 的产物）；空串表示无会话</param>
    /// <returns>房间绝对路径；无会话时为空串（= 无豁免）</returns>
    public static string GetRoomAbsolutePath(string folderName) =>
        string.IsNullOrWhiteSpace(folderName) ? string.Empty : Path.Combine(RootPath, folderName);

    /// <summary>
    /// 删除某个会话的全部产出（会话被删除时调用）：新布局里删整间房间、顺手收掉空的工作区家目录；
    /// 旧布局（<c>Outputs/{标题}_{id8}</c>，ADR 0026 起不迁移）按 id 后缀通配清残留。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public static void DeleteAll(string sessionId) =>
        DeleteAll(RootPath, AppPaths.Data.AgentOutputs, sessionId);

    /// <inheritdoc cref="DeleteAll(string)"/>
    /// <param name="workspacesRoot">新布局根目录（显式入参，可单测）</param>
    /// <param name="legacyRoot">旧布局根目录（显式入参，可单测）</param>
    /// <param name="sessionId">会话标识</param>
    public static void DeleteAll(string workspacesRoot, string legacyRoot, string sessionId)
    {
        string id = Sanitize(sessionId, IdLength);
        if (id.Length == 0) return;

        // 新布局:两层(工作区家 → 房间)。房间删完,家目录空了就一并收掉
        if (Directory.Exists(workspacesRoot))
        {
            foreach (string home in Directory.EnumerateDirectories(workspacesRoot))
            {
                try
                {
                    string room = Path.Combine(home, id);
                    if (Directory.Exists(room)) Directory.Delete(room, recursive: true);
                }
                catch (Exception e)
                {
                    //删不掉只是留下垃圾,不该让删会话本身失败
                    Log.Warning($"Agent outputs: delete room '{home}/{{{id}}}' failed: {e.Message}");
                }
                DeleteIfEmpty(home);
            }
        }

        // 旧布局:不迁移,但会话删除时残留要清。按 id 后缀通配,改过名留下的多个目录一并清掉
        if (Directory.Exists(legacyRoot))
        {
            foreach (string path in Directory.EnumerateDirectories(legacyRoot))
            {
                string name = Path.GetFileName(path);
                if (!string.Equals(name, id, StringComparison.Ordinal) &&
                    !name.EndsWith($"_{id}", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(path, recursive: true);
                }
                catch (Exception e)
                {
                    Log.Warning($"Agent outputs: delete '{name}' failed: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 启动清扫：删掉产物树里的空目录（ADR 0026）。<b>只删空目录，不碰任何有内容的数据</b>——
    /// 这是对 ADR 0019「不自动清理」的补丁而不是推翻（那条针对的是不删用户数据）。
    /// </summary>
    public static void SweepEmptyDirectories() =>
        SweepEmptyDirectories(RootPath, AppPaths.Data.AgentOutputs);

    /// <inheritdoc cref="SweepEmptyDirectories"/>
    /// <param name="workspacesRoot">新布局根目录（显式入参，可单测）</param>
    /// <param name="legacyRoot">旧布局根目录（显式入参，可单测）</param>
    public static void SweepEmptyDirectories(string workspacesRoot, string legacyRoot)
    {
        // 旧布局:一层 {标题}_{id8},删空的
        SweepOneLevel(legacyRoot);

        // 新布局:先删空房间,家目录由此变空的再删
        if (!Directory.Exists(workspacesRoot)) return;
        foreach (string home in Directory.EnumerateDirectories(workspacesRoot))
        {
            SweepOneLevel(home);
            DeleteIfEmpty(home);
        }
    }

    /// <summary>删掉某目录下所有<b>空</b>的直接子目录</summary>
    private static void SweepOneLevel(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (string path in Directory.EnumerateDirectories(root))
        {
            DeleteIfEmpty(path);
        }
    }

    /// <summary>目录存在且为空才删。空目录没有数据,删错也不可能丢东西</summary>
    private static void DeleteIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && IsEmpty(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Agent outputs: prune empty '{directory}' failed: {e.Message}");
        }
    }

    private static bool IsEmpty(string directory) =>
        !Directory.EnumerateFileSystemEntries(directory).Any();

    /// <summary>目录名安全化：只留字母与数字（中文属 Letter，会保留），再截到长度上限</summary>
    private static string Sanitize(string text, int maxLength)
    {
        string kept = new(text.Where(char.IsLetterOrDigit).ToArray());
        return kept.Length <= maxLength ? kept : kept[..maxLength];
    }
}
