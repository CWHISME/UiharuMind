using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace UiharuMind.Features.ScreenCapture.Frames;

/// <summary>
/// 一帧冻结的整屏画面，覆盖范围恰为某一块屏幕。
///
/// 抽掉这层是因为各平台的整屏来源天差地别：Windows 走 DXGI 逐屏拿到 HPPH 像素数组，
/// Linux 只能从 xdg-desktop-portal 拿一张覆盖整个桌面的 PNG 再裁到目标屏，
/// macOS 用 screencapture 按屏静默抓取（point 与像素差着 backing 倍率，帧内自行换算）。
/// 而选区遮罩窗需要的只有两件事——一张铺满窗口的底图，以及按矩形裁剪的能力，
/// 于是把差异全部关在这个接口后面。
/// </summary>
public interface IScreenFrame : IDisposable
{
    /// <summary>
    /// 本帧底图，像素尺寸与所属屏幕的物理像素一致。<b>所有权归本对象</b>，
    /// 调用方不得释放，也不得在本对象释放后继续引用。
    /// <para>
    /// <b>位图一律按 96 DPI 构造（Size == PixelSize），各实现不得用 DPI 伪装尺寸。</b>
    /// Avalonia 的 Image 按 <c>Bitmap.Size</c>（DIP）算源矩形，Skia 后端却把源矩形当物理像素采样
    /// （见 <c>DrawingContextImpl.DrawBitmap</c>），两端口径不一致：给 Retina 抓帧盖上 192 DPI
    /// 让 Size 回到 point，画出来就只有左上角四分之一被放大铺满。
    /// point 与像素的换算一律交给调用方按屏幕信息显式处理。
    /// </para>
    /// </summary>
    Bitmap Display { get; }

    /// <summary>本帧左上角在桌面坐标系中的位置</summary>
    PixelPoint Origin { get; }

    /// <summary>本帧的像素尺寸</summary>
    PixelSize PixelSize { get; }

    /// <summary>
    /// 裁剪出一块新位图。<b>调用方接管返回的位图</b>
    /// </summary>
    /// <param name="desktopRegion">裁剪区域，使用屏幕坐标系（与 Screen.Bounds 同口径：
    /// Windows 下像素，macOS 下 point），内部自行减去 Origin 并换算到像素</param>
    /// <returns>裁剪结果（同样是 96 DPI 的像素位图）；区域非法或裁剪失败返回 null</returns>
    Bitmap? Crop(PixelRect desktopRegion);

    /// <summary>
    /// 取屏幕坐标系中一点的颜色，用于放大镜取色。与 Crop 同口径，各实现内部自行换算到像素。
    /// </summary>
    /// <param name="screenUnits">屏幕坐标系坐标</param>
    /// <returns>颜色；越界或失败返回 null</returns>
    Color? SampleColor(PixelPoint screenUnits);
}
