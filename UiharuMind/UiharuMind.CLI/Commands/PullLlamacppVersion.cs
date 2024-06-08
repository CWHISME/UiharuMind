using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using UiharuMind.Core;

namespace UiharuMind.CLI.Commands;

[Command("pull", Description = "pull latest version info.")]
public class PullLlamacppVersion : ICommand
{
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var version = await UiharuCoreManager.Instance.LLamaCppServer.PullLastestVersion();
        
    }
}