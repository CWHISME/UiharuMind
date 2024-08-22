using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using UiharuMind.Core;

namespace UiharuMind.CLI.Commands;

[Command("list", Description = "List local model.")]
public class ListModelCommand : ICommand
{
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var list = await UiharuCoreManager.Instance.LLamaCppServer.GetModelList();
        int i = 1;
        foreach (var model in list)
        {
            await console.Output.WriteLineAsync($"{i++}. {model.Key}");
        }
    }
}