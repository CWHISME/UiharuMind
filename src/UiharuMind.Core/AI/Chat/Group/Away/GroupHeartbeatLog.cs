using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>
/// 无限模式心跳日志（ADR 0055 §8）：化身输出里整行以 ⟦…⟧ 包裹的心跳状态行，
/// 不进群（<c>GroupChatCoordinator.Avatar</c> 的 PostBody 剥掉），追加写进这一份文件，
/// 留给用户与巡检读——谁在阻碍、卡在哪、试过什么。
/// 路径与群流水同一间草稿目录（群壳那一间），产物枚举排除它（见 <c>GroupArtifacts</c>），
/// 不参与「本群产物」指纹，免得保险丝与退避永不触发。
/// </summary>
public static class GroupHeartbeatLog
{
    /// <summary>心跳日志文件固定名（草稿目录里）：<see cref="GroupHeartbeatLog"/> 用它落盘，产物枚举排除它</summary>
    public const string FileName = "心跳.md";

    /// <summary>
    /// 心跳日志路径：与产物区同一间草稿目录（群壳那一间，成员与化身的 $DRAFT 都指它）
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <returns>绝对路径</returns>
    public static string PathOf(ChatSession group) =>
        Path.Combine(GroupArtifacts.DraftRoomOf(group), FileName);

    /// <summary>
    /// 追加一条心跳状态行。文件不存在时先写标题头；进程内互斥，同一时间只许一个追加。
    /// 写失败不抛：心跳是观察用的簿记，丢了不影响群推进，只记告警
    /// </summary>
    /// <param name="group">群壳会话（取标题与草稿目录）</param>
    /// <param name="heartbeatLine">整行心跳（⟦…⟧）</param>
    public static void Append(ChatSession group, string heartbeatLine)
    {
        string path = PathOf(group);
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path))
                    File.WriteAllText(path, $"# {group.Title} 心跳\n\n");
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {heartbeatLine}\n");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Group heartbeat log append '{path}' failed: {e.Message}");
        }
    }

    private static readonly object Sync = new();
}