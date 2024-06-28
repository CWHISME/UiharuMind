using System.Drawing;
using System.Runtime.InteropServices;
using CliWrap;

namespace UiharuMind.Core.ScreenCapture;

public class ScreenCaptureMac
{
    public static async Task Capture()
    {
        await Cli.Wrap("screencapture").WithArguments("-i -x -c capturecache").ExecuteAsync();
    }

    public static async Task CaptureWindow()
    {
        //窗口
        await Cli.Wrap("screencapture").WithArguments("-i -x -c -w -o capturecache").ExecuteAsync();
    }
}