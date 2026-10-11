/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Features.ScreenCapture.Ocr;
using UiharuMind.Platform.Windows.Desktop;
using UiharuMind.Platform.Windows.Ocr;

namespace UiharuMind.Platform.Windows;

/// <summary>
/// 把 Windows 的平台实现挂到应用上（ADR 0084）。由桌面入口在启动最早处调用，界面与扩展只认接口
/// </summary>
public static class WindowsPlatform
{
    /// <summary>
    /// 挂上桌面扩展的平台层与 OCR
    /// </summary>
    public static void Install()
    {
        DesktopHost.UsePlatform(() => new WindowsDesktopPlatform());
        OcrRecognizerFactory.Use(() => new WindowsOcrRecognizer());
    }
}
