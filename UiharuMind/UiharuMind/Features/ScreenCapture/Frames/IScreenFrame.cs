using System;
using Avalonia;
using Avalonia.Media.Imaging;

namespace UiharuMind.Features.ScreenCapture.Frames;

/// <summary>
/// 一帧冻结的整屏画面，覆盖范围恰为某一块屏幕。
///
/// 抽掉这层是因为各平台的整屏来源天差地别：Windows 走 DXGI 逐屏拿到 HPPH 像素数组，
/// Linux 只能从 xdg-desktop-portal 拿一张覆盖整个桌面的 PNG 再裁到目标屏。
/// 而选区遮罩窗需要的只有两件事——一张铺满窗口的底图，以及按矩形裁剪的能力，
/// 于是把差异全部关在这个接口后面。
/// </summary>
public interface IScreenFrame : IDisposable
{
    /// <summary>
    /// 本帧底图，尺寸与所属屏幕一致。<b>所有权归本对象</b>，
    /// 调用方不得释放，也不得在本对象释放后继续引用。
    /// </summary>
    Bitmap Display { get; }

    /// <summary>本帧左上角在桌面坐标系中的位置</summary>
    PixelPoint Origin { get; }

    /// <summary>本帧的像素尺寸</summary>
    PixelSize PixelSize { get; }

    /// <summary>
    /// 裁剪出一块新位图。<b>调用方接管返回的位图</b>
    /// </summary>
    /// <param name="desktopRegion">裁剪区域，使用桌面绝对像素坐标（内部自行减去 Origin）</param>
    /// <returns>裁剪结果；区域非法或裁剪失败返回 null</returns>
    Bitmap? Crop(PixelRect desktopRegion);
}
