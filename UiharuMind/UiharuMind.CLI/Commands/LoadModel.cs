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

using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using CliWrap.Exceptions;
using UiharuMind.Core;
using UiharuMind.Core.AI;
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
        // var list = await LlmManager.Instance.RuntimeEngineManager.LLamaCppServer.GetModelList();
        // if (list == null) throw new CommandException("model list not found.");
        // GGufModelInfo? model = null;
        // if (int.TryParse(OrderOrName, out var order))
        // {
        //     int index = order - 1;
        //     if (index < 0 || index >= list.Count) throw new CommandException($"order:{OrderOrName} invalid!");
        //     // model = list.fi//list[index];
        //     int numIndex = 0;
        //     foreach (var item in list)
        //     {
        //         if (numIndex == index)
        //         {
        //             model = item.Value;
        //             break;
        //         }
        //
        //         numIndex++;
        //     }
        // }
        // // else model = list.FirstOrDefault(x => x.ModelName == OrderOrName);
        // else
        //     list.TryGetValue(OrderOrName, out model);
        //
        // if (model == null) throw new CommandException($"model:{OrderOrName} not found.");
        // await LlmManager.Instance.RuntimeEngineManager.LLamaCppServer.StartServer(model.ModelPath, Port);
    }
}