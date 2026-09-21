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

    /// <summary>
    /// 静默抓取指定序号的显示器到文件（无系统 UI、无声音），供自家选区遮罩窗做冻结底图。
    /// 序号规则同 screencapture -D：1 为主屏，2 起为副屏。
    /// </summary>
    /// <param name="displayIndex">显示器序号，1 起</param>
    /// <param name="filePath">输出 PNG 路径</param>
    /// <returns>抓取成功返回 True</returns>
    public static async Task<bool> CaptureDisplayToFile(int displayIndex, string filePath)
    {
        return await ProcessHelper.StartProcess("screencapture", $"-x -D{displayIndex} \"{filePath}\"");
    }
}