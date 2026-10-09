/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 记忆清单（ADR 0076）：工作区记忆目录里每份笔记一行「文件名 — 首行摘要」。
///
/// 清单作为<b>快照</b>挂在本轮输入消息的注解上（<see cref="ChatMessageAnnotations.MemoryIndexSnapshot"/>），
/// 发送时才由 <see cref="MemoryIndexProjection"/> 并进那条消息——历史里的正文仍是用户原话，
/// 气泡、搜索、编辑、交接回忆都不必认识它。快照一旦挂上就不再变，前缀缓存因此不受记忆读写影响。
/// </summary>
public static class MemoryIndex
{
    /// <summary>首行摘要上限（按字符元素计），超出截断并补省略号</summary>
    public const int SummaryMaxLength = 50;

    /// <summary>整份清单字数上限可设的最小值（设置页与手改配置都按它夹）</summary>
    public const int MinListBudget = 500;

    /// <summary>整份清单字数上限可设的最大值</summary>
    public const int MaxListBudget = 20000;

    /// <summary>清单每一行的前缀，计数据此认行</summary>
    private const string EntryPrefix = "- ";

    private const int HeadBytes = 4096; //只读文件开头这么多字节找首行

    /// <summary>
    /// 本轮输入还没有清单可看时，给它挂上一份当前快照。
    /// 「可看」以供给窗口为准：会话开头、交接之后（窗口从交接文档起）、功能上线前的旧会话都会补挂一次，
    /// 已挂过的窗口不再挂——同一窗口里只有一份，且不随之后的记忆读写变化。
    /// </summary>
    /// <param name="history">会话完整历史（本轮输入尚未落入）</param>
    /// <param name="input">本轮输入消息</param>
    /// <param name="memoryDirectory">记忆目录绝对路径</param>
    /// <param name="memoryToken">记忆目录简写，清单里的路径按它写</param>
    /// <param name="budget">整份清单的字数上限（设置项）</param>
    /// <returns>挂上了为 true</returns>
    public static bool TryAnchor(IReadOnlyList<ChatMessage> history, IReadOnlyList<ChatMessage> input,
        string memoryDirectory, string memoryToken, int budget)
    {
        if (input.Any(HasSnapshot)) return false; //重新生成会把挂过的原消息再送一遍
        for (int i = HistoryHandoff.SupplyStartIndex(history); i < history.Count; i++)
        {
            if (HasSnapshot(history[i])) return false;
        }

        // 审批回应也是 user 角色,但它是控制消息,不是这一轮的输入
        ChatMessage? anchor = input.FirstOrDefault(x =>
            x.Role == ChatRole.User && !x.Contents.Any(c => c is ToolApprovalResponseContent));
        if (anchor == null) return false;

        string? list = Build(memoryDirectory, memoryToken, budget);
        if (list == null) return false;

        anchor.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        anchor.AdditionalProperties[ChatMessageAnnotations.MemoryIndexSnapshot] = list;
        return true;
    }

    /// <summary>
    /// 按目录现状生成清单：递归找 <c>.md</c>，按修改时间从新到旧，超出上限的写一行说明。
    /// 路径写成简写开头（<c>$MEMORY/a.md</c>），模型照抄就能 Read——只给文件名时它会自己猜目录（冒烟实测猜错）
    /// </summary>
    /// <param name="memoryDirectory">记忆目录绝对路径</param>
    /// <param name="memoryToken">记忆目录简写；空串时只写相对文件名（Read $MEMORY 的索引）</param>
    /// <param name="budget">整份清单的字数上限，夹在 <see cref="MinListBudget"/> 与 <see cref="MaxListBudget"/> 之间</param>
    /// <returns>清单正文；目录不存在或没有笔记时为 null</returns>
    public static string? Build(string memoryDirectory, string memoryToken, int budget)
    {
        if (string.IsNullOrEmpty(memoryDirectory) || !Directory.Exists(memoryDirectory)) return null;
        budget = Math.Clamp(budget, MinListBudget, MaxListBudget);

        List<FileInfo> files;
        try
        {
            files = new DirectoryInfo(memoryDirectory)
                .EnumerateFiles("*.md", SearchOption.AllDirectories)
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception e)
        {
            Log.Warning($"Scan memory directory failed: {e.Message}");
            return null;
        }

        if (files.Count == 0) return null;

        StringBuilder list = new();
        int listed = 0;
        foreach (FileInfo file in files)
        {
            string relative = Path.GetRelativePath(memoryDirectory, file.FullName).Replace('\\', '/');
            string name = memoryToken.Length > 0 ? $"{memoryToken}/{relative}" : relative;
            string summary = SummaryOf(ReadHead(file.FullName));
            string line = summary.Length > 0 ? $"{EntryPrefix}{name} — {summary}" : $"{EntryPrefix}{name}";
            if (listed > 0 && list.Length + line.Length > budget) break;

            list.AppendLine(line);
            listed++;
        }

        if (listed < files.Count) list.AppendLine($"（另有 {files.Count - listed} 份较早修改的未列出，用 Glob 查看）");
        return list.ToString().TrimEnd();
    }

    /// <summary>
    /// 笔记的首行摘要：第一行非空文字，去掉 Markdown 标题井号，超过 <see cref="SummaryMaxLength"/> 截断
    /// </summary>
    /// <param name="text">笔记正文（或它的开头）</param>
    /// <returns>摘要；没有文字为空串</returns>
    public static string SummaryOf(string text)
    {
        string? first = text.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
        if (first == null) return string.Empty;

        first = first.TrimStart('#').Trim();
        StringInfo info = new(first);
        return info.LengthInTextElements <= SummaryMaxLength
            ? first
            : info.SubstringByTextElements(0, SummaryMaxLength) + "…";
    }

    /// <summary>
    /// 清单里列了几份笔记
    /// </summary>
    /// <param name="list">清单正文</param>
    /// <returns>份数</returns>
    public static int CountEntries(string list) =>
        list.Split('\n').Count(x => x.StartsWith(EntryPrefix, StringComparison.Ordinal));

    private static bool HasSnapshot(ChatMessage message) =>
        ChatMessageAnnotations.MemoryIndexSnapshotOf(message) != null;

    private static string ReadHead(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] buffer = new byte[Math.Min(HeadBytes, stream.Length)];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            ReadOnlySpan<byte> head = buffer.AsSpan(0, read);
            TextEncodingProbe probe = TextEncodingDetector.Detect(head, isComplete: read == stream.Length);
            return probe.Verdict is ETextEncodingVerdict.Binary or ETextEncodingVerdict.Unknown
                ? string.Empty
                : probe.Decode(head);
        }
        catch (Exception e)
        {
            Log.Warning($"Read memory note head failed '{path}': {e.Message}");
            return string.Empty;
        }
    }
}
