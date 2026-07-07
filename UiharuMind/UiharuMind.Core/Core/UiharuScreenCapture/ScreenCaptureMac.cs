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

using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.UiharuScreenCapture;

public class ScreenCaptureMac
{
    public static async Task<bool> Capture()
    {
        return await ProcessHelper.StartProcess("screencapture", "-i -x -c capturecache");
    }

    public static async Task<bool> Capture(int screenId)
    {
        return await ProcessHelper.StartProcess("screencapture", $"-x -c -D {screenId} capturecache");
    }

    public static async Task<bool> CaptureWindow()
    {
        //窗口
        return await ProcessHelper.StartProcess("screencapture", "-i -x -c -w -o capturecache");
        // await Cli.Wrap("screencapture").WithArguments("-i -x -c -w -o capturecache").ExecuteAsync();
    }
}