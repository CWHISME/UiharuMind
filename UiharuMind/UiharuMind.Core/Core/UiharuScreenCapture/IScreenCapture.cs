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

namespace UiharuMind.Core.Core.UiharuScreenCapture;

public interface IScreenCapture
{
    /// <summary>
    /// 截取指定的整个屏幕
    /// </summary>
    /// <param name="screenId"></param>
    /// <returns></returns>
    public Task Capture(int screenId);
}