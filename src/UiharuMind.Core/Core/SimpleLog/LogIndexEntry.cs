/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core.SimpleLog;

/// <summary>正文落在哪条流上</summary>
public enum ELogStream
{
    /// <summary>时间线主流 Log.txt</summary>
    Main,

    /// <summary>外置正文 Bodies.txt</summary>
    Bodies,
}

/// <summary>
/// 内存里代表一条日志的<b>定位信息 + 预览</b>。大小与正文体量无关，
/// 因此内存占用只随<b>条数</b>增长，不随字节数增长。
///
/// ⚠️ 索引<b>只覆盖当前进程写入的内容</b>，进程启动时不回读历史文件。
/// </summary>
/// <param name="Stream">正文所在的流</param>
/// <param name="FileId">流内的文件代号，滚动时递增</param>
/// <param name="Offset">正文起点的字节偏移</param>
/// <param name="ByteLength">正文的字节长度</param>
/// <param name="LogType">级别</param>
/// <param name="Category">内容性质</param>
/// <param name="Time">产生时刻</param>
/// <param name="Preview">首行预览，列表行显示的就是它</param>
public sealed record LogIndexEntry(
    ELogStream Stream,
    int FileId,
    long Offset,
    int ByteLength,
    ELogType LogType,
    ELogCategory Category,
    DateTime Time,
    string Preview);
