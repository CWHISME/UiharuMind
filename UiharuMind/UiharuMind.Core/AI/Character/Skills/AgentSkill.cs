using Microsoft.SemanticKernel;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.Skills;

public abstract class AgentSkill
{
    private Dictionary<string, object?>? _args;

    public void AddParams(string key, object? value)
    {
        _args ??= new Dictionary<string, object?>();
        _args[key] = value;
    }

    public IAsyncEnumerable<string> DoSkill(ModelRunningData? modelRunningData, string userInput,
        CancellationToken cancellationToken = default)
    {
        if (modelRunningData is not { IsRunning: true })
        {
            return new AsyncEnumerableWithMessage("Model is not running.");
        }

        return OnDoSkill(modelRunningData, userInput, _args, cancellationToken);
    }

    protected abstract IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string userInput,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default);
}