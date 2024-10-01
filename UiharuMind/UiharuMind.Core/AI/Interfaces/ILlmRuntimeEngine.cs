using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.AI.Interfaces;

public interface ILlmRuntimeEngine
{
    public Task Run(VersionInfo info, ILlmModel model, Action<float>? onLoading = null, Action? onLoadOver = null,
        CancellationToken token = default);
}