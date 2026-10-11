/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.ScreenCapture.Ocr;

namespace UiharuMind.Platform.Windows.Ocr;

/// <summary>
/// Windows 的截图 OCR（ADR 0084）：OneOCR 优先，起不来或这次失败就静默退回 Windows.Media.Ocr
/// </summary>
public sealed class WindowsOcrRecognizer : IOcrTextRecognizer
{
    private static readonly Lazy<OneOcrEngine?> OneOcr = new(OneOcrEngine.TryCreate); //全应用一份，第一次识别时才找文件、起引擎

    /// <inheritdoc />
    public async Task<IReadOnlyList<OcrTextLine>> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default)
    {
        (byte[] pixels, int width, int height) = Bgra(image);
        try
        {
            return await Task.Run(async () =>
            {
                if (OneOcr.Value is { } engine)
                {
                    try
                    {
                        return engine.Recognize(pixels, width, height);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"OneOCR failed, using Windows.Media.Ocr: {e.Message}");
                    }
                }

                return await WindowsMediaOcr.RecognizeAsync(pixels, width, height);
            }, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warning($"OCR failed: {e.Message}");
            return [];
        }
    }

    // 读出 BGRA 像素：两个引擎都收 32 位，系统 OCR 还要求 BGRA 顺序
    private static (byte[] Pixels, int Width, int Height) Bgra(Bitmap image)
    {
        PixelSize size = image.PixelSize;
        int stride = size.Width * 4;
        byte[] pixels = new byte[stride * size.Height];
        GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            image.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        if (image.Format == PixelFormats.Rgba8888)
        {
            for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }

        return (pixels, size.Width, size.Height);
    }
}
