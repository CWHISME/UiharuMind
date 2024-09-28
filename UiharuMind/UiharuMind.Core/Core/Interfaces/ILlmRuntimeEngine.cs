namespace UiharuMind.Core.Core.Interfaces;

public interface ILlmRuntimeEngine
{
    public Task Run(ILlmModel model, Action<CancellationTokenSource> onStartLoad, Action<float>? onLoading = null,
        Action? onLoadOver = null);
}