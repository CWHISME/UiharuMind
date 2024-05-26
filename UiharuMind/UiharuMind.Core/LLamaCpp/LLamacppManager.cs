using CliWrap;
using UiharuMind.Core.Core.Singleton;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppManager : Singleton<LLamaCppManager>
{
    public async void ScanModels()
    {
        await Cli.Wrap("./llama-b2860-bin-win-vulkan-x64/lookup-stats").
            WithArguments("-h").
            ExecuteAsync();
    }
}