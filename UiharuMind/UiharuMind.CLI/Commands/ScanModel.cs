using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using UiharuMind.Core;

namespace UiharuMind.CLI.Commands;

[Command("scan", Description = "Force Scan local model.")]
public class ScanModelCommand : ICommand
{
    // // Order: 0
    // [CommandParameter(0, Description = "Value whose logarithm is to be found.")]
    // public required double Value { get; init; }

    // Name: --force
    // Short name: -f
    // [CommandOption("force", 'f', Description = "force scan.")]
    // public bool Force { get; init; } = false;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var infos = await UiharuCoreManager.Instance.LLamaCppServer.ScanLocalModels(true);
        int i = 1;
        foreach (var model in infos)
        {
            await console.Output.WriteLineAsync($"{i++}. {model.Key}");
        }
    }
}