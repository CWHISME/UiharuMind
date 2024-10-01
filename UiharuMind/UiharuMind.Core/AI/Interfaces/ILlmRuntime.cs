namespace UiharuMind.Core.AI.Interfaces;

public interface ILlmRuntime
{
    public Task Run(ILlmModel model, Action<float>? onLoading = null, Action? onLoadOver = null,CancellationToken token = default);
}