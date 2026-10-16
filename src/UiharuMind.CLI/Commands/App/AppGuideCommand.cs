using System.Text.Json.Nodes;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using UiharuMind.Core.Core.DevControl;

namespace UiharuMind.CLI.Commands.App;

/// <summary>
/// <c>app guide</c>：拉 uiharu-dev 技能正文（经命令行驱动应用的完整说明）并拆包打印，省去 <c>app call app.guide</c> 的 JSON 包装
/// </summary>
[Command("app guide", Description = "Print the uiharu-dev skill body: the full guide to driving the app from the shell.")]
public partial class AppGuideCommand : ICommand
{
    [CommandOption("home", Description = "Profile directory; defaults to UIHARU_HOME or ~/.uiharu.")]
    public string? Home { get; set; }

    [CommandOption("timeout", Description = "Seconds to wait for the reply; 0 waits forever.")]
    public int Timeout { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        using CancellationTokenSource timeout = AppControl.TimeoutOf(Timeout);
        await using var client = await AppControl.ConnectAsync(AppControl.EndpointOf(Home), timeout.Token);
        JsonObject reply = await client.CallAsync("app.guide", null, timeout.Token);
        if (reply["ok"]?.GetValue<bool>() != true) throw new CommandException(reply["error"]?.GetValue<string>() ?? "step failed", 1);
        await console.Output.WriteLineAsync(reply["result"]?.GetValue<string>() ?? string.Empty);
    }
}