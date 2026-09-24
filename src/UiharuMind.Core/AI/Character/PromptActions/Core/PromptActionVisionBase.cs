using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.PromptActions;

public abstract class PromptActionVisionBase : PromptActionConvertableBase
{
    protected readonly IReadOnlyList<ImageInput> _images;

    // 识图场景的用户文字是「要回答的问题」，不是待处理的数据：
    // 包进 source_text 属机制误用，且视觉类模板从未声明该边界，会把标签漏进输出
    // （与 CustomPromptAction 同一理由，见 PromptActionConvertableBase.WrapUserInput）
    protected override bool WrapUserInput => false;

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
