/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>
/// 一条日志的<b>全部内容</b>。正文不设上限——一条几 MB 的 LLM 请求正文也是一条完整条目。
///
/// ⚠️ 不要与 <see cref="LogIndexEntry"/> 混淆：条目是<b>磁盘上</b>的东西，且只在写入途中
/// 短暂存在于内存；索引项才是常驻内存的那个。运行期内存里<b>没有</b>条目的完整正文。
/// </summary>
public sealed class LogItem
{
    /// <summary>级别</summary>
    public ELogType LogType { get; }

    /// <summary>内容性质</summary>
    public ELogCategory Category { get; }

    /// <summary>产生时刻</summary>
    public DateTime Time { get; }

    /// <summary>正文，不截断</summary>
    public string Text { get; }

    public LogItem(ELogType type, string text, ELogCategory category = ELogCategory.General)
    {
        LogType = type;
        Category = category;
        Time = DateTime.Now;
        Text = text;
    }

    /// <summary>落盘与控制台共用同一种形态：头行 + 原样正文</summary>
    public override string ToString() => LogFormat.Header(this) + Environment.NewLine + Text;
}
