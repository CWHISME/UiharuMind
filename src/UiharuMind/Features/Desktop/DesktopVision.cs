/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SkiaSharp;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Features.ScreenCapture.Ocr;

namespace UiharuMind.Features.Desktop;

/// <summary>
/// 桌面扩展的看图（ADR 0084）：认字用截图 OCR 那一套（按平台挑实现），画编号框用 SkiaSharp
/// </summary>
public sealed class DesktopVision : IDesktopVision
{
    private const int MaxSide = 1600; //交给模型的图长边不超过这么多像素：再大只是更贵
    private const int JpegQuality = 80;

    private readonly IOcrTextRecognizer _recognizer;

    /// <param name="recognizer">文字识别；缺省按平台挑</param>
    public DesktopVision(IOcrTextRecognizer? recognizer = null)
    {
        _recognizer = recognizer ?? OcrRecognizerFactory.Create();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DesktopTextLine>> RecognizeAsync(byte[] png, CancellationToken cancellationToken)
    {
        using Bitmap image = new(new MemoryStream(png));
        IReadOnlyList<OcrTextLine> lines = await _recognizer.RecognizeAsync(image, cancellationToken);
        return lines.Where(line => line.Text.Trim().Length > 0)
            .Select(line => new DesktopTextLine(line.Text.Trim(), new AxRect(line.Box.X, line.Box.Y, line.Box.Width, line.Box.Height)))
            .ToList();
    }

    /// <inheritdoc />
    public byte[] Mark(byte[] png, IReadOnlyList<DesktopMark> marks)
    {
        using SKBitmap original = SKBitmap.Decode(png);
        float scale = Math.Min(1f, (float)MaxSide / Math.Max(original.Width, original.Height));
        int width = Math.Max(1, (int)(original.Width * scale));
        int height = Math.Max(1, (int)(original.Height * scale));
        using SKBitmap resized = original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        using SKCanvas canvas = new(resized);

        using SKPaint frame = new() { Color = new SKColor(255, 120, 0), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        using SKPaint tag = new() { Color = new SKColor(255, 120, 0), Style = SKPaintStyle.Fill };
        using SKPaint label = new() { Color = SKColors.White, IsAntialias = true };
        using SKFont font = new(SKTypeface.Default, 13);
        foreach (DesktopMark mark in marks)
        {
            SKRect box = SKRect.Create((float)mark.Box.X * width, (float)mark.Box.Y * height,
                (float)mark.Box.Width * width, (float)mark.Box.Height * height);
            canvas.DrawRect(box, frame);
            // 编号贴在框的左上角外侧，不压住框里的字
            float textWidth = font.MeasureText(mark.Label);
            SKRect background = SKRect.Create(box.Left, Math.Max(0, box.Top - 16), textWidth + 6, 16);
            canvas.DrawRect(background, tag);
            canvas.DrawText(mark.Label, background.Left + 3, background.Bottom - 3, SKTextAlign.Left, font, label);
        }

        using SKImage image = SKImage.FromBitmap(resized);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data.ToArray();
    }
}
