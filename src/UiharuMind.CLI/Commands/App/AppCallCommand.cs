using System.Text.Json.Nodes;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using UiharuMind.Core.Core.DevControl;

namespace UiharuMind.CLI.Commands.App;

[Command("app call", Description = "Run one dev step in the running app and print the reply as JSON.")]
public partial class AppCallCommand : ICommand
{
    [CommandParameter(0, Description = "Step name, e.g. group.post (app.ops lists them all).")]
    public required string Op { get; set; }

    [CommandOption("args", Description = "Step arguments as a JSON object.")]
    public string? Args { get; set; }

    [CommandOption("home", Description = "Profile directory; defaults to UIHARU_HOME or ~/.uiharu.")]
    public string? Home { get; set; }

    [CommandOption("timeout", Description = "Seconds to wait for the reply; 0 waits forever. The step keeps running in the app either way.")]
    public int Timeout { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        JsonNode? args = AppControl.ParseArgs(Args);
        using CancellationTokenSource timeout = AppControl.TimeoutOf(Timeout);
        await using var client = await AppControl.ConnectAsync(AppControl.EndpointOf(Home), timeout.Token);

        JsonObject reply;
        try
        {
            reply = await client.CallAsync(Op, args, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new CommandException($"no reply within {Timeout}s; '{Op}' is still running in the app", 3);
        }
        catch (IOException e)
        {
            throw new CommandException($"lost the app while waiting for '{Op}': {e.Message}", 2);
        }

        await console.Output.WriteLineAsync(reply.ToJsonString(DevControlServer.ReplyOptions));
        if (reply["ok"]?.GetValue<bool>() != true) throw new CommandException(reply["error"]?.GetValue<string>() ?? "step failed", 1);
    }
}
