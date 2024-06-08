using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using CliWrap.Exceptions;
using UiharuMind.Core;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.CLI.Commands;

[Command("load", Description = "load local model.")]
public class LoadModelCommand : ICommand
{
    [CommandParameter(0, Description = "order or model name.")]
    public required string OrderOrName { get; init; }

    [CommandParameter(1, Description = "port.", IsRequired = false)]
    public int Port { get; init; } = 1369;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var list = await UiharuCoreManager.Instance.LLamaCppServer.GetModelList();
        if (list == null) throw new CommandException("model list not found.");
        GGufModelInfo? model = null;
        if (int.TryParse(OrderOrName, out var order))
        {
            int index = order - 1;
            if (index < 0 || index >= list.Count) throw new CommandException($"order:{OrderOrName} invalid!");
            model = list[index];
        }
        else model = list.FirstOrDefault(x => x.ModelName == OrderOrName);

        if (model == null) throw new CommandException($"model:{OrderOrName} not found.");
        await UiharuCoreManager.Instance.LLamaCppServer.StartServer(model.ModelPath, Port);
    }
}