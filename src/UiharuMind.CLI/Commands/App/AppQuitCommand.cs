using System.Diagnostics;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;

namespace UiharuMind.CLI.Commands.App;

[Command("app quit", Description = "Quit the running app the same way the tray menu does.")]
public partial class AppQuitCommand : ICommand
{
    [CommandOption("home", Description = "Profile directory; defaults to UIHARU_HOME or ~/.uiharu.")]
    public string? Home { get; set; }

    private const int QuitTimeoutSeconds = 30;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var endpoint = AppControl.EndpointOf(Home);
        int? pid = await AppControl.PingAsync(endpoint);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(QuitTimeoutSeconds));
        await using (var client = await AppControl.ConnectAsync(endpoint, timeout.Token))
        {
            await client.CallAsync("app.quit", token: timeout.Token);
        }

        // socket 在收尾一开头就删了，进程还要收完轮次才退：按进程号等
        while (pid is { } id && IsRunning(id))
        {
            if (timeout.IsCancellationRequested)
                throw new CommandException($"quit sent, but pid {id} is still running after {QuitTimeoutSeconds}s", 3);
            await Task.Delay(200);
        }

        await console.Output.WriteLineAsync("app exited");
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
