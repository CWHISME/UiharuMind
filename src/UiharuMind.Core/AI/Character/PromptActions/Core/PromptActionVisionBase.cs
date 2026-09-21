using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.AI.Character.PromptActions;

public abstract class PromptActionVisionBase : PromptActionConvertableBase
{
    protected readonly byte[] _imageBytes;

    public PromptActionVisionBase(byte[] imageBytes)
    {
        _imageBytes = imageBytes;
    }
    
    protected override IAsyncEnumerable<string> OnRunAsync(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        if (!modelRunningData.IsVisionModel)
        {
            return new AsyncEnumerableWithMessage("Not support vision model.");
        }

        return RunTransientAsync(text, args, _imageBytes, cancellationToken);
    }

    protected override bool IsVision => true;
}
