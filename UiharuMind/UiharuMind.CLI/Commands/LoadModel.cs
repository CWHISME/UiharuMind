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
using CliFx.Infrastructure;

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
        await Task.CompletedTask;
    }
}
