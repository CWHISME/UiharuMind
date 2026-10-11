/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools.Accessibility;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop;

/// <summary>
/// 看图的两样本事（ADR 0084）：认图里的字、在图上画编号框。识别与绘图在界面层（系统 OCR、SkiaSharp），
/// 由组装入口挂到 <see cref="DesktopHost.Vision"/>；没挂时桌面扩展只读无障碍树
/// </summary>
public interface IDesktopVision
{
    /// <summary>
    /// 认出图里的文字行
    /// </summary>
    /// <param name="png">PNG 数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文字行（阅读顺序）；认不出或这个系统没有 OCR 为空</returns>
    Task<IReadOnlyList<DesktopTextLine>> RecognizeAsync(byte[] png, CancellationToken cancellationToken);

    /// <summary>
    /// 在图上画编号框（Set-of-Marks），压成 JPEG：看图的模型回答「是 e12」，不必报像素坐标
    /// </summary>
    /// <param name="png">PNG 数据</param>
    /// <param name="marks">编号与框（归一化坐标，0..1，左上为原点）</param>
    /// <returns>JPEG 数据（长边缩到适合交给模型的大小）</returns>
    byte[] Mark(byte[] png, IReadOnlyList<DesktopMark> marks);
}

/// <summary>
/// 一行认出的字
/// </summary>
/// <param name="Text">文字</param>
/// <param name="Box">框（归一化坐标，0..1，左上为原点）</param>
public sealed record DesktopTextLine(string Text, AxRect Box);

/// <summary>
/// 图上的一个编号框
/// </summary>
/// <param name="Label">编号（e12）</param>
/// <param name="Box">框（归一化坐标，0..1，左上为原点）</param>
public sealed record DesktopMark(string Label, AxRect Box);
