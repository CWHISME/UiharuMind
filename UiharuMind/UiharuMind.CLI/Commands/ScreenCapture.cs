using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using UiharuMind.Core;

namespace UiharuMind.CLI.Commands;

[Command("capture", Description = "Screen Capture.")]
public class ScreenCaptureCommand : ICommand
{
    public async ValueTask ExecuteAsync(IConsole console)
    {
        // await UiharuCoreManager.Instance.CaptureScreen();
        // await console.Output.WriteLineAsync("Capture Screen.");
    }
}