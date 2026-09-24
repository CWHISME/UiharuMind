using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.PromptActions;

public abstract class PromptActionVisionBase : PromptActionConvertableBase
{
    protected readonly IReadOnlyList<ImageInput> _images;

    public PromptActionVisionBase(IReadOnlyList<ImageInput> images)
    {
        _images = images;
    }

    protected override IAsyncEnumerable<string> OnRunAsync(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        if (!modelRunningData.IsVisionModel)
        {
            return new AsyncEnumerableWithMessage("Not support vision model.");
        }

        return RunTransientAsync(text, args, _images, cancellationToken);
    }

    protected override bool IsVision => true;
}
