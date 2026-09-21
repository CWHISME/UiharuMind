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
using CliFx.Binding;
using CliFx.Infrastructure;

namespace UiharuMind.CLI.Commands;

[Command("scan", Description = "Force Scan local model.")]
public partial class ScanModelCommand : ICommand
{
    public async ValueTask ExecuteAsync(IConsole console)
    {
        await Task.CompletedTask;
    }
}
