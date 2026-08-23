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

public interface IScreenCapture
{
    /// <summary>
    /// 交互式截取一块区域，返回 PNG 流；用户取消或失败时返回 null。
    /// 返回的流由调用方负责释放。
    /// </summary>
    /// <returns>PNG 图像流，或 null</returns>
    Task<Stream?> CaptureRegionAsync();
}