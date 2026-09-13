using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.ScreenCapture.Ocr.Mac;

/// <summary>
/// macOS 系统 Vision OCR（VNRecognizeTextRequest，accurate 档）。
/// 纯 ObjC 运行时 P/Invoke，不引入 Xamarin.Mac；只经 <see cref="OcrRecognizerFactory"/> 在 macOS 上实例化。
/// 刻意不用 completion-handler block：plain init + 同步 perform 后直接读 results，
/// 省掉在 C# 里手工搭 ObjC block 的复杂度。
/// </summary>
public sealed class MacVisionOcrRecognizer : IOcrTextRecognizer
{
    // 识别偏好语言（capcap 同款）：CJK + 英，之外的语种靠 automaticallyDetectsLanguage 兜底
    private static readonly string[] PreferredLanguages = { "zh-Hans", "zh-Hant", "en-US" };

    public async Task<IReadOnlyList<OcrTextLine>> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default)
    {
        // Vision 这边只吃文件：借一张临时 PNG，识别完即删，不进剪贴板历史
        string tempPath = Path.Combine(Path.GetTempPath(), $"uiharu-ocr-{Guid.NewGuid():N}.png");
        try
        {
            image.Save(tempPath);
        }
        catch (Exception e)
        {
            Log.Warning($"OCR 临时文件写入失败：{e.Message}");
            return Array.Empty<OcrTextLine>();
        }

        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    return RecognizeCore(tempPath);
                }
                catch (Exception e)
                {
                    Log.Warning($"Vision OCR 失败：{e.Message}");
                    return (IReadOnlyList<OcrTextLine>)Array.Empty<OcrTextLine>();
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Warning($"删除 OCR 临时文件失败：{e.Message}");
        }
    }

    private static IReadOnlyList<OcrTextLine> RecognizeCore(string imagePath)
    {
        // .NET 线程池线程没有 autoreleasepool，不推一个的话自动释放对象会泄漏
        IntPtr pool = ObjcAutoreleasePoolPush();
        try
        {
            byte[] utf8Path = Encoding.UTF8.GetBytes(imagePath);
            IntPtr url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, utf8Path, utf8Path.Length, false);
            if (url == IntPtr.Zero) return Array.Empty<OcrTextLine>();
            try
            {
                IntPtr imageSource = CGImageSourceCreateWithURL(url, IntPtr.Zero);
                if (imageSource == IntPtr.Zero) return Array.Empty<OcrTextLine>();
                try
                {
                    IntPtr cgImage = CGImageSourceCreateImageAtIndex(imageSource, UIntPtr.Zero, IntPtr.Zero);
                    if (cgImage == IntPtr.Zero) return Array.Empty<OcrTextLine>();
                    try
                    {
                        return PerformRequest(cgImage);
                    }
                    finally
                    {
                        CGImageRelease(cgImage);
                    }
                }
                finally
                {
                    CFRelease(imageSource);
                }
            }
            finally
            {
                CFRelease(url);
            }
        }
        finally
        {
            ObjcAutoreleasePoolPop(pool);
        }
    }

    private static IReadOnlyList<OcrTextLine> PerformRequest(IntPtr cgImage)
    {
        IntPtr handler = IntPtr.Zero;
        IntPtr request = IntPtr.Zero;
        IntPtr languages = IntPtr.Zero;
        IntPtr requestArray = IntPtr.Zero;
        try
        {
            IntPtr handlerClass = ObjcGetClass("VNImageRequestHandler");
            handler = ObjcMsgSendPtr(
                ObjcMsgSendPtr(handlerClass, Sel("alloc")),
                Sel("initWithCGImage:options:"), cgImage, IntPtr.Zero);

            IntPtr requestClass = ObjcGetClass("VNRecognizeTextRequest");
            request = ObjcMsgSendPtr(ObjcMsgSendPtr(requestClass, Sel("alloc")), Sel("init"));
            if (request == IntPtr.Zero) return Array.Empty<OcrTextLine>();

            ObjcMsgSendLong(request, Sel("setRecognitionLevel:"), 0); // Accurate
            ObjcMsgSendBool(request, Sel("setUsesLanguageCorrection:"), true);
            ObjcMsgSendBool(request, Sel("setAutomaticallyDetectsLanguage:"), true);

            languages = ObjcMsgSendPtr(ObjcMsgSendPtr(ObjcGetClass("NSMutableArray"), Sel("alloc")), Sel("init"));
            foreach (string language in PreferredLanguages)
                ObjcMsgSendVoid(languages, Sel("addObject:"), NsString(language));
            ObjcMsgSendPtr(request, Sel("setRecognitionLanguages:"), languages);

            requestArray = ObjcMsgSendPtr(ObjcMsgSendPtr(ObjcGetClass("NSMutableArray"), Sel("alloc")), Sel("init"));
            ObjcMsgSendVoid(requestArray, Sel("addObject:"), request);

            byte ok = ObjcMsgSendRequest(handler, Sel("performRequests:error:"), requestArray, IntPtr.Zero);
            if (ok == 0) return Array.Empty<OcrTextLine>();

            return ReadResults(request);
        }
        finally
        {
            if (requestArray != IntPtr.Zero) ObjcMsgSendVoid(requestArray, Sel("release"));
            if (languages != IntPtr.Zero) ObjcMsgSendVoid(languages, Sel("release"));
            if (request != IntPtr.Zero) ObjcMsgSendVoid(request, Sel("release"));
            if (handler != IntPtr.Zero) ObjcMsgSendVoid(handler, Sel("release"));
        }
    }

    private static IReadOnlyList<OcrTextLine> ReadResults(IntPtr request)
    {
        IntPtr results = ObjcMsgSendPtr(request, Sel("results"));
        if (results == IntPtr.Zero) return Array.Empty<OcrTextLine>();

        ulong count = ObjcMsgSendULong(results, Sel("count"));
        var lines = new List<OcrTextLine>((int)Math.Min(count, 256));
        for (ulong i = 0; i < count; i++)
        {
            IntPtr observation = ObjcMsgSendPtr(results, Sel("objectAtIndex:"), i);
            if (observation == IntPtr.Zero) continue;

            CGRect box = MsgSendRect(observation, Sel("boundingBox"));
            string text = TopCandidateText(observation);
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Vision 是左下原点归一化，换算成左上原点并钳制
            var rect = new Rect(
                Clamp01(box.X),
                Clamp01(1.0 - box.Y - box.Height),
                Clamp01(box.Width),
                Clamp01(box.Height));
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            lines.Add(new OcrTextLine(text.Trim(), rect));
        }

        // 阅读顺序：纵向差超过阈值算不同行（上先下后），同行内左先右先（capcap 同款）
        lines.Sort((a, b) =>
        {
            double midA = a.Box.Center.Y;
            double midB = b.Box.Center.Y;
            if (Math.Abs(midA - midB) > 0.012) return midA.CompareTo(midB);
            return a.Box.X.CompareTo(b.Box.X);
        });
        return lines;
    }

    private static string TopCandidateText(IntPtr observation)
    {
        IntPtr candidates = ObjcMsgSendPtr(observation, Sel("topCandidates:"), (ulong)1);
        if (candidates == IntPtr.Zero) return "";
        if (ObjcMsgSendULong(candidates, Sel("count")) == 0) return "";

        IntPtr candidate = ObjcMsgSendPtr(candidates, Sel("objectAtIndex:"), (ulong)0);
        if (candidate == IntPtr.Zero) return "";
        IntPtr nsText = ObjcMsgSendPtr(candidate, Sel("string"));
        if (nsText == IntPtr.Zero) return "";
        IntPtr utf8 = ObjcMsgSendPtr(nsText, Sel("UTF8String"));
        if (utf8 == IntPtr.Zero) return "";
        return Marshal.PtrToStringUTF8(utf8) ?? "";
    }

    private static IntPtr NsString(string value)
    {
        return ObjcMsgSendString(ObjcGetClass("NSString"), Sel("stringWithUTF8String:"), value);
    }

    private static double Clamp01(double value)
    {
        if (value < 0) return 0;
        if (value > 1) return 1;
        return value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    private static IntPtr Sel(string name)
    {
        return SelRegisterName(name);
    }

    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ImageIOLibrary = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string selectorName);

    [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string className);

    [DllImport(ObjCLibrary, EntryPoint = "objc_autoreleasePoolPush")]
    private static extern IntPtr ObjcAutoreleasePoolPush();

    [DllImport(ObjCLibrary, EntryPoint = "objc_autoreleasePoolPop")]
    private static extern void ObjcAutoreleasePoolPop(IntPtr pool);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtr(IntPtr receiver, IntPtr selector, ulong arg);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendString(IntPtr receiver, IntPtr selector,
        [MarshalAs(UnmanagedType.LPStr)] string arg);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendVoid(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendVoid(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendBool(IntPtr receiver, IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendLong(IntPtr receiver, IntPtr selector, long value);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern ulong ObjcMsgSendULong(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern byte ObjcMsgSendRequest(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    // ARM64 上没有 stret 入口，objc_msgSend 直接回传结构体；x86_64 才走 stret。
    // 两个 DllImport 都是延迟解析的，声明了也不会在另一架构上炸
    private static CGRect MsgSendRect(IntPtr receiver, IntPtr selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return ObjcMsgSendRect(receiver, selector);
        ObjcMsgSendStret(out CGRect rect, receiver, selector);
        return rect;
    }

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern CGRect ObjcMsgSendRect(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend_stret")]
    private static extern void ObjcMsgSendStret(out CGRect rect, IntPtr receiver, IntPtr selector);

    [DllImport(CoreFoundationLibrary, EntryPoint = "CFURLCreateFromFileSystemRepresentation")]
    private static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte[] buffer, nint bufLen,
        [MarshalAs(UnmanagedType.I1)] bool isDirectory);

    [DllImport(CoreFoundationLibrary, EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr obj);

    [DllImport(ImageIOLibrary, EntryPoint = "CGImageSourceCreateWithURL")]
    private static extern IntPtr CGImageSourceCreateWithURL(IntPtr url, IntPtr options);

    [DllImport(ImageIOLibrary, EntryPoint = "CGImageSourceCreateImageAtIndex")]
    private static extern IntPtr CGImageSourceCreateImageAtIndex(IntPtr source, UIntPtr index, IntPtr options);

    [DllImport(ImageIOLibrary, EntryPoint = "CGImageRelease")]
    private static extern void CGImageRelease(IntPtr image);
}
