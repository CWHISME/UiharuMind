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
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.ScreenCapture.Ocr;

namespace UiharuMind.Platform.Windows.Ocr;

/// <summary>
/// OneOCR：Windows 11 截图工具内置的识别引擎，中文好、带行框（ADR 0084）。
/// 接口是逆向得来的（社区 b1tg/win11-oneocr，参照 MIT 的 Starshot、Zaya.OCR），截图工具更新后可能失效，所以随时准备退回系统 OCR
/// </summary>
internal sealed unsafe class OneOcrEngine
{
    // 模型文件的解密密钥，已在多个 MIT 项目里公开
    private const string ModelKey = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";
    private const int MaxLines = 1000;
    private const int ImageTypeRgba = 3;

    private readonly object _lock = new(); //一条流水线不保证能并发跑
    private readonly IntPtr _pipeline;
    private readonly IntPtr _options;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, RawImage*, IntPtr, out IntPtr, int> _run;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, out long, int> _lineCount;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, long, out IntPtr, int> _line;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, out IntPtr, int> _lineContent;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, out IntPtr, int> _lineBox;
    private readonly delegate* unmanaged[Cdecl]<IntPtr, void> _release; //老版本没导出它，为空

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RawImage
    {
        public int Type;
        public int Width;
        public int Height;
        public int Reserved;
        public long Step;
        public IntPtr Data;
    }

    // 四个角点：左上、右上、右下、左下
    [StructLayout(LayoutKind.Sequential)]
    private struct RawBox
    {
        public float X1;
        public float Y1;
        public float X2;
        public float Y2;
        public float X3;
        public float Y3;
        public float X4;
        public float Y4;
    }

    private OneOcrEngine(string directory)
    {
        // onnxruntime 由 oneocr 按名字加载：先从同一目录载进来，免得找到系统里别的版本
        NativeLibrary.Load(Path.Combine(directory, OneOcrFiles.Runtime));
        IntPtr library = NativeLibrary.Load(Path.Combine(directory, OneOcrFiles.Library));

        var createInit = (delegate* unmanaged[Cdecl]<out IntPtr, int>)NativeLibrary.GetExport(library, "CreateOcrInitOptions");
        var delayLoad = (delegate* unmanaged[Cdecl]<IntPtr, byte, int>)NativeLibrary.GetExport(library, "OcrInitOptionsSetUseModelDelayLoad");
        var createPipeline = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, out IntPtr, int>)NativeLibrary.GetExport(library, "CreateOcrPipeline");
        var createOptions = (delegate* unmanaged[Cdecl]<out IntPtr, int>)NativeLibrary.GetExport(library, "CreateOcrProcessOptions");
        var maxLines = (delegate* unmanaged[Cdecl]<IntPtr, long, int>)NativeLibrary.GetExport(library, "OcrProcessOptionsSetMaxRecognitionLineCount");
        _run = (delegate* unmanaged[Cdecl]<IntPtr, RawImage*, IntPtr, out IntPtr, int>)NativeLibrary.GetExport(library, "RunOcrPipeline");
        _lineCount = (delegate* unmanaged[Cdecl]<IntPtr, out long, int>)NativeLibrary.GetExport(library, "GetOcrLineCount");
        _line = (delegate* unmanaged[Cdecl]<IntPtr, long, out IntPtr, int>)NativeLibrary.GetExport(library, "GetOcrLine");
        _lineContent = (delegate* unmanaged[Cdecl]<IntPtr, out IntPtr, int>)NativeLibrary.GetExport(library, "GetOcrLineContent");
        _lineBox = (delegate* unmanaged[Cdecl]<IntPtr, out IntPtr, int>)NativeLibrary.GetExport(library, "GetOcrLineBoundingBox");
        _release = NativeLibrary.TryGetExport(library, "ReleaseOcrResult", out IntPtr release)
            ? (delegate* unmanaged[Cdecl]<IntPtr, void>)release
            : null;

        Check(createInit(out IntPtr init), "CreateOcrInitOptions");
        Check(delayLoad(init, 0), "OcrInitOptionsSetUseModelDelayLoad");
        // 路径与密钥按系统代码页传（引擎收的是 char*）
        IntPtr model = Marshal.StringToHGlobalAnsi(Path.Combine(directory, OneOcrFiles.Model));
        IntPtr key = Marshal.StringToHGlobalAnsi(ModelKey);
        try
        {
            Check(createPipeline(model, key, init, out _pipeline), "CreateOcrPipeline");
        }
        finally
        {
            Marshal.FreeHGlobal(model);
            Marshal.FreeHGlobal(key);
        }

        Check(createOptions(out _options), "CreateOcrProcessOptions");
        Check(maxLines(_options, MaxLines), "OcrProcessOptionsSetMaxRecognitionLineCount");
    }

    /// <summary>
    /// 本机有截图工具或照片应用、引擎也起得来时建一个
    /// </summary>
    /// <returns>引擎；用不了为 null（原因记日志）</returns>
    public static OneOcrEngine? TryCreate()
    {
        try
        {
            string? directory = OneOcrFiles.Prepare();
            if (directory == null)
            {
                Log.Debug("OneOCR not found on this machine (no Snipping Tool or Photos app); using Windows.Media.Ocr");
                return null;
            }

            return new OneOcrEngine(directory);
        }
        catch (Exception e)
        {
            Log.Warning($"OneOCR could not start, using Windows.Media.Ocr: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 识别
    /// </summary>
    /// <param name="pixels">32 位像素（通道顺序不影响结果）</param>
    /// <param name="width">宽</param>
    /// <param name="height">高</param>
    /// <returns>行，框归一化</returns>
    public IReadOnlyList<OcrTextLine> Recognize(byte[] pixels, int width, int height)
    {
        lock (_lock)
        {
            fixed (byte* data = pixels)
            {
                RawImage image = new() { Type = ImageTypeRgba, Width = width, Height = height, Step = width * 4L, Data = (IntPtr)data };
                Check(_run(_pipeline, &image, _options, out IntPtr result), "RunOcrPipeline");
                try
                {
                    return ReadLines(result, width, height);
                }
                finally
                {
                    if (_release != null) _release(result);
                }
            }
        }
    }

    private List<OcrTextLine> ReadLines(IntPtr result, int width, int height)
    {
        Check(_lineCount(result, out long count), "GetOcrLineCount");
        List<OcrTextLine> lines = new();
        for (long i = 0; i < count; i++)
        {
            if (_line(result, i, out IntPtr line) != 0 || _lineContent(line, out IntPtr content) != 0) continue;
            string text = Marshal.PtrToStringUTF8(content) ?? string.Empty;
            if (text.Trim().Length == 0 || _lineBox(line, out IntPtr boxPointer) != 0) continue;
            RawBox box = *(RawBox*)boxPointer;
            // 斜着的字给的是四边形：取外接矩形
            float left = Math.Min(Math.Min(box.X1, box.X2), Math.Min(box.X3, box.X4));
            float right = Math.Max(Math.Max(box.X1, box.X2), Math.Max(box.X3, box.X4));
            float top = Math.Min(Math.Min(box.Y1, box.Y2), Math.Min(box.Y3, box.Y4));
            float bottom = Math.Max(Math.Max(box.Y1, box.Y2), Math.Max(box.Y3, box.Y4));
            lines.Add(new OcrTextLine(text.Trim(), new Rect(left / width, top / height, (right - left) / width, (bottom - top) / height)));
        }

        return lines;
    }

    private static void Check(int code, string step)
    {
        if (code != 0) throw new InvalidOperationException($"OneOCR {step} failed ({code})");
    }
}
