using Microsoft.SemanticKernel;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.Skills;

public abstract class AgentSkill
{
    public IAsyncEnumerable<string> DoSkill(ModelRunningData? modelRunningData, string userInput,
        CancellationToken cancellationToken = default)
    {
        if (modelRunningData is not { IsRunning: true })
        {
            return new AsyncEnumerableWithMessage("Model is not running.");
        }

        return OnDoSkill(modelRunningData, userInput, cancellationToken);
    }

    protected abstract IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string userInput,
        CancellationToken cancellationToken = default);
}