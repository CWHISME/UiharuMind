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
/// agent 产出（跑 Python 画的图、导出的数据）的磁盘布局：<b>一个会话一个目录</b>，
/// 目录名是 <c>{会话标题}_{会话id前8位}</c>。
///
/// 分会话是<b>正确性问题而非整洁问题</b>：产出以绝对路径写进对话历史，扁平共享时两个会话
/// 各画一张 <c>chart.png</c>，后者会静默盖掉前者——旧对话回头看一切正常，只是图是错的。
///
/// <b>与 <see cref="Tools.Memory.FileMemoryLayout"/> 的关键差别：改名不搬目录。</b>
/// 那边的目录只被框架的活状态引用，搬走无碍；这边的路径已经<b>逐字写进历史消息</b>，
/// 一搬就是一片打不开的图。代价是同一个会话改名后会多出一个目录，旧图仍留在旧目录里
/// ——这是"路径永不失效"换来的，刻意接受。清理靠 id 后缀通配，见 <see cref="DeleteAll"/>。
/// </summary>
public static class AgentOutputLayout
{
    private const int MaxTitleLength = 32; //目录名里标题部分的长度上限

    /// <summary>目录名里会话 id 的保留长度</summary>
    private const int IdLength = 8;

    /// <summary>所有会话的产出目录的父目录</summary>
    public static string RootPath => AppPaths.Data.AgentOutputs;

    /// <summary>
    /// 会话的产出目录名（相对 <see cref="RootPath"/>）
    /// </summary>
    /// <param name="sessionTitle">会话标题（用户可读的那一半）</param>
    /// <param name="sessionId">会话标识</param>
    /// <returns>目录名，形如 <c>标题_id8</c>；标题里没有可用字符时只剩 id</returns>
    public static string GetFolderName(string sessionTitle, string sessionId)
    {
        // id 截到 8 位:会话 id 是 guid("N"),前 8 位撞车的概率在个人应用的量级上可以忽略。
        // FileMemoryLayout 那边不截是因为角色 id 是枚举名,Assistant/AssistantExpert 共前缀,一截就撞
        string suffix = Sanitize(sessionId, IdLength);
        if (suffix.Length == 0) return string.Empty;

        string title = Sanitize(sessionTitle, MaxTitleLength);
        return title.Length == 0 ? suffix : $"{title}_{suffix}";
    }

    /// <summary>
    /// 删除某个会话的全部产出目录（会话被删除时调用）。
    /// 按 id 后缀通配，因此改过名、留下多个目录的情况也能一并清掉。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public static void DeleteAll(string sessionId)
    {
        string suffix = Sanitize(sessionId, IdLength);
        if (suffix.Length == 0 || !Directory.Exists(RootPath)) return;

        foreach (string path in Directory.EnumerateDirectories(RootPath))
        {
            string name = Path.GetFileName(path);
            if (!string.Equals(name, suffix, StringComparison.Ordinal) &&
                !name.EndsWith($"_{suffix}", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception e)
            {
                //删不掉只是留下垃圾,不该让删会话本身失败
                Log.Warning($"Agent outputs: delete '{name}' failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// 目录名安全化：只留字母与数字（中文属 Letter，会保留），再截到长度上限。
    /// 标题部分截断后可能与另一个会话相同，但 id 后缀负责区分。
    /// </summary>
    private static string Sanitize(string text, int maxLength)
    {
        string kept = new(text.Where(char.IsLetterOrDigit).ToArray());
        return kept.Length <= maxLength ? kept : kept[..maxLength];
    }
}
