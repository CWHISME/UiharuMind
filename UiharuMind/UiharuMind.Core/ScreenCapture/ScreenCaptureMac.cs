using System.Drawing;
using System.Runtime.InteropServices;
using CliWrap;
using UiharuMind.Core.Core.Logs;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.ScreenCapture;

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

    public static async Task CaptureWindow()
    {
        //窗口
        await ProcessHelper.StartProcess("screencapture", "-i -x -c -w -o capturecache");
        // await Cli.Wrap("screencapture").WithArguments("-i -x -c -w -o capturecache").ExecuteAsync();
    }
}