using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace UiharuMind.Features.ScreenCapture.Ocr;

/// <summary>
/// 占位实现：当前平台没有可用的系统 OCR，直接返回空。
/// </summary>
public sealed class NullOcrRecognizer : IOcrTextRecognizer
{
    public Task<IReadOnlyList<OcrTextLine>> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OcrTextLine>>(Array.Empty<OcrTextLine>());
    }
}
