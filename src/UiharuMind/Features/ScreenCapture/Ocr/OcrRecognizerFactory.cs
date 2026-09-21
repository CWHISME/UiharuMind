using UiharuMind.Core.Core.Utils;
using UiharuMind.Features.ScreenCapture.Ocr.Mac;

namespace UiharuMind.Features.ScreenCapture.Ocr;

/// <summary>
/// 按平台挑选 OCR 实现。选型只收敛在这一个工厂里，调用方不写平台分支。
/// </summary>
public static class OcrRecognizerFactory
{
    /// <summary>
    /// 当前平台是否有可用的系统 OCR（无 key、无网络、离线）。
    /// </summary>
    public static bool IsSupported => PlatformUtils.IsMacOS;

    /// <summary>
    /// 创建当前平台的识别器；不支持的平台返回空实现（识别结果恒为空）。
    /// </summary>
    public static IOcrTextRecognizer Create()
    {
        if (PlatformUtils.IsMacOS) return new MacVisionOcrRecognizer();
        return new NullOcrRecognizer();
    }
}
