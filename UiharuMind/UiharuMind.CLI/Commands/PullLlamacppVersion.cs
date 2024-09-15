using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using UiharuMind.Core;
using UiharuMind.Core.AI;

namespace UiharuMind.CLI.Commands;

[Command("pull", Description = "pull latest version info.")]
public class PullLlamacppVersion : ICommand
{
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var version = await LlmManager.Instance.LLamaCppServer.PullLastestVersion();
        
    }
}