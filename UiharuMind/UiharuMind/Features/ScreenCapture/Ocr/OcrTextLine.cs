using Avalonia;

namespace UiharuMind.Features.ScreenCapture.Ocr;

/// <summary>
/// 一行识别出的文字。Box 是归一化坐标（0..1），原点在左上，与 Avalonia 布局同口径；
/// 各平台实现在内部把原生坐标系换算好，调用方不再区分平台。
/// </summary>
/// <param name="Text">行文本</param>
/// <param name="Box">行包围盒（归一化，左上原点）</param>
public sealed record OcrTextLine(string Text, Rect Box);
