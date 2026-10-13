/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 一处编辑：把文件里唯一出现的 <see cref="OldString"/> 换成 <see cref="NewString"/>。
/// 本类型直接作为 <c>Edit</c> 工具的参数形状，属性说明就是模型看到的 schema 文案。
/// </summary>
public sealed class FileEdit
{
    /// <summary>被替换的原文，必须在文件中唯一出现，且不得与同一次调用的其他编辑重叠</summary>
    [Description("被替换的原文，必须在文件中唯一出现，且不得与同一次调用的其他编辑重叠。")]
    public string OldString { get; set; } = string.Empty;

    /// <summary>替换后的文本（空串表示删掉这段）</summary>
    [Description("替换后的文本，空字符串表示删掉命中的那段。")]
    public string NewString { get; set; } = string.Empty;
}

/// <summary>
/// 一次编辑的干跑结果：成功带新正文与 diff，失败带一句给模型的话术。
///
/// 工具执行与审批卡片预演拿到的是<b>同一个对象</b>，所以两边不可能对同一次调用说出不同的话，
/// 也不可能出现「卡片显示改得挺好、落盘却失败」。
/// </summary>
public sealed class FileEditPlan
{
    private FileEditPlan(string? error, string newText, IReadOnlyList<LineDiffEntry> diff,
        TextFileEnvelope envelope, bool strippedLinePrefixes = false, int totalEdits = 0, int appliedEdits = 0,
        string? skippedNote = null, IReadOnlyList<int>? skippedIndexes = null)
    {
        SkippedIndexes = skippedIndexes ?? [];
        TotalEdits = totalEdits;
        AppliedEdits = appliedEdits;
        SkippedNote = skippedNote;
        StrippedLinePrefixes = strippedLinePrefixes;
        Error = error;
        NewText = newText;
        Diff = diff;
        Envelope = envelope;
    }

    /// <summary>失败原因（中文，直接进模型上下文，也直接显示在审批卡片上）；成功时为 null</summary>
    public string? Error { get; }

    /// <summary>是否剥掉过模型抄进参数的 Read 行号前缀（成功回执里说一声，下次别再带）</summary>
    public bool StrippedLinePrefixes { get; }

    /// <summary>是否可以落盘</summary>
    public bool Succeeded => Error is null;

    /// <summary>编辑总条数</summary>
    public int TotalEdits { get; }

    /// <summary>会生效的条数；少于 <see cref="TotalEdits"/> 即部分写入</summary>
    public int AppliedEdits { get; }

    /// <summary>部分写入时没生效条目的话术（对着写入后的新文本算，行号与落盘后的文件一致）；全部生效时为 null</summary>
    public string? SkippedNote { get; }

    /// <summary>没生效的条目序号（审批卡片据此照实标出）</summary>
    public IReadOnlyList<int> SkippedIndexes { get; }

    /// <summary>是否部分写入</summary>
    public bool IsPartial => SkippedNote != null;

    /// <summary>变更的行级 diff（带行号，按变更块聚合，块之间的上下文已合并）</summary>
    public IReadOnlyList<LineDiffEntry> Diff { get; }

    /// <summary>落盘用的新正文（原文行尾原样保留，只有新插入的文本按文件风格转写）</summary>
    internal string NewText { get; }

    /// <summary>读入时的落盘保真信封</summary>
    internal TextFileEnvelope Envelope { get; }

    internal static FileEditPlan Failed(string error) => new(error, string.Empty, [], default);

    internal static FileEditPlan Success(string newText, IReadOnlyList<LineDiffEntry> diff,
        TextFileEnvelope envelope, bool strippedLinePrefixes, int totalEdits, int appliedEdits, string? skippedNote,
        IReadOnlyList<int> skippedIndexes) =>
        new(null, newText, diff, envelope, strippedLinePrefixes, totalEdits, appliedEdits, skippedNote, skippedIndexes);
}
