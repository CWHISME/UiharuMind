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
    public static async Task Capture()
    {
        try
        {
            await ProcessHelper.StartProcess("screencapture", "-i -x -c capturecache");
            // await Cli.Wrap("screencapture").WithArguments("-i -x -c capturecache").ExecuteAsync();
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    public static async Task Capture(int screenId)
    {
        try
        {
            await ProcessHelper.StartProcess("screencapture", $"-x -c -D {screenId} capturecache");
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    public static async Task CaptureWindow()
    {
        //窗口
        await ProcessHelper.StartProcess("screencapture", "-i -x -c -w -o capturecache");
        // await Cli.Wrap("screencapture").WithArguments("-i -x -c -w -o capturecache").ExecuteAsync();
    }
}