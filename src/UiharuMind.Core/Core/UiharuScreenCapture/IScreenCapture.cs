/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.IO;
using System.Threading.Tasks;

namespace UiharuMind.Core.Core.UiharuScreenCapture;

/// <summary>
/// 整屏抓取能力。选区交互由应用自绘的遮罩窗完成，后端只负责交出一张原图，
/// 这样放大镜、实时贴图、OCR 等自有交互在各平台保持一致。
/// </summary>
public interface IScreenCapture
{
    /// <summary>
    /// 抓取整个桌面，返回 PNG 流；失败时返回 null。
    /// </summary>
    /// <param name="parentWindowHandle">
    /// 供系统授权对话框定位的父窗口句柄。X11/XWayland 下为 "x11:0x{窗口ID}"，纯 Wayland 下为 "wayland:"。
    /// </param>
    /// <returns>PNG 图像流（由调用方释放），或 null</returns>
    Task<Stream?> CaptureFullScreenAsync(string parentWindowHandle);
}
