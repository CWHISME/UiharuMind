/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Avalonia;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.ScreenCapture.Ocr;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace UiharuMind.Platform.Windows.Ocr;

/// <summary>
/// 系统自带的 OCR（Windows.Media.Ocr）：公开、稳定，按用户的界面语言识别；OneOCR 用不了时的兜底
/// </summary>
internal static class WindowsMediaOcr
{
    private static readonly Lazy<OcrEngine?> Engine = new(OcrEngine.TryCreateFromUserProfileLanguages);

    /// <summary>
    /// 识别
    /// </summary>
    /// <param name="bgra">BGRA 像素</param>
    /// <param name="width">宽</param>
    /// <param name="height">高</param>
    /// <returns>行，框归一化；没有可用的语言为空</returns>
    public static async Task<IReadOnlyList<OcrTextLine>> RecognizeAsync(byte[] bgra, int width, int height)
    {
        if (Engine.Value is not { } engine)
        {
            Log.Warning("Windows.Media.Ocr has no recognizer for the user's languages");
            return [];
        }

        if (Math.Max(width, height) > OcrEngine.MaxImageDimension)
        {
            Log.Warning($"Image {width}×{height} is larger than Windows.Media.Ocr accepts ({OcrEngine.MaxImageDimension})");
            return [];
        }

        using SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, width, height,
            BitmapAlphaMode.Premultiplied);
        OcrResult result = await engine.RecognizeAsync(bitmap);
        List<OcrTextLine> lines = new();
        foreach (OcrLine line in result.Lines)
        {
            if (line.Words.Count == 0) continue;
            double left = line.Words.Min(word => word.BoundingRect.X);
            double top = line.Words.Min(word => word.BoundingRect.Y);
            double right = line.Words.Max(word => word.BoundingRect.X + word.BoundingRect.Width);
            double bottom = line.Words.Max(word => word.BoundingRect.Y + word.BoundingRect.Height);
            lines.Add(new OcrTextLine(Join(line.Words.Select(word => word.Text)),
                new Rect(left / width, top / height, (right - left) / width, (bottom - top) / height)));
        }

        return lines;
    }

    // 中文按字给词：两个汉字之间不加空格，其余照常空格分开
    private static string Join(IEnumerable<string> words)
    {
        StringBuilder text = new();
        foreach (string word in words)
        {
            if (word.Length == 0) continue;
            if (text.Length > 0 && !(IsCjk(text[^1]) && IsCjk(word[0]))) text.Append(' ');
            text.Append(word);
        }

        return text.ToString();
    }

    private static bool IsCjk(char character) =>
        character is >= '　' and <= '鿿' or >= '豈' and <= '﫿' or >= '＀' and <= '￯';
}
