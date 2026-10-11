using System;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Features.ScreenCapture.Ocr.Mac;

namespace UiharuMind.Features.ScreenCapture.Ocr;

/// <summary>
/// 按平台挑选 OCR 实现。选型只收敛在这一个工厂里，调用方不写平台分支。
/// Windows 的实现在单独的程序集里（要 -windows 目标框架），由组装入口在启动时经 <see cref="Use"/> 挂上
/// </summary>
public static class OcrRecognizerFactory
{
    private static Func<IOcrTextRecognizer>? _platformFactory; //组装入口挂上的平台实现

    /// <summary>
    /// 当前平台是否有可用的系统 OCR（无 key、无网络、离线）。
    /// </summary>
    public static bool IsSupported => _platformFactory != null || PlatformUtils.IsMacOS;

    /// <summary>
    /// 挂上平台实现（Windows），须在第一次 <see cref="Create"/> 之前
    /// </summary>
    /// <param name="factory">造识别器</param>
    public static void Use(Func<IOcrTextRecognizer> factory) => _platformFactory = factory;

    /// <summary>
    /// 创建当前平台的识别器；不支持的平台返回空实现（识别结果恒为空）。
    /// </summary>
    public static IOcrTextRecognizer Create()
    {
        if (_platformFactory != null) return _platformFactory();
        if (PlatformUtils.IsMacOS) return new MacVisionOcrRecognizer();
        return new NullOcrRecognizer();
    }
}
