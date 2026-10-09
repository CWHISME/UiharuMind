using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.ImageGeneration.Dialects;
using UiharuMind.Core.AI.Runtime;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 本地生图模型怎么出图：交给 stable-diffusion.cpp 引擎。失败照生图回退的口径分类（ADR 0052、0074）：
/// 引擎没装、缺组件、起不来、崩溃、任务失败、超时都算「这一家用不了」——本地不花钱，换下一家只是多等；
/// 参数不合法算「请求本身不行」
/// </summary>
/// <param name="engine">引擎（第一次用才建）</param>
/// <param name="modelRoot">模型目录</param>
public sealed class LocalImageDialect(Func<SdCppEngine> engine, Func<string> modelRoot) : IImageDialect
{
    /// <summary>
    /// 走共享引擎与当前模型目录的那一个
    /// </summary>
    public static LocalImageDialect Shared { get; } = new(() => SdCppEngine.Shared, () => LocalImageModels.ModelRoot);

    public async Task<ImageOutcome> GenerateAsync(ImageModelInfo model, ImageRequest request,
        IProgress<ImageProgress>? progress, CancellationToken ct)
    {
        if (model.Local is not { } local)
            return ImageOutcome.Fail(EImageFailureKind.Unavailable, $"'{model.Name}' is not a local model");

        try
        {
            IReadOnlyList<byte[]> images = await engine().GenerateAsync(LocalImageModels.ToSetup(model, modelRoot()),
                LocalImageModels.ToRequest(local, request), progress, ct).ConfigureAwait(false);
            return new ImageOutcome { Images = images.Select(x => new ImageOutput(x, null)).ToList() };
        }
        catch (SdCppRequestException e)
        {
            return ImageOutcome.Fail(EImageFailureKind.Rejected, $"local engine rejected the request: {e.Message}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (LocalEngineNotReadyException)
        {
            return ImageOutcome.Fail(EImageFailureKind.Unavailable, "the local image engine is not installed");
        }
        catch (LocalServerException e)
        {
            return ImageOutcome.Fail(EImageFailureKind.Unavailable, $"local image engine failed: {e.Detail}");
        }
        catch (Exception e) when (e is SdCppModelMissingException or SdCppGenerationException or TimeoutException
                                      or HttpRequestException or OperationCanceledException)
        {
            return ImageOutcome.Fail(EImageFailureKind.Unavailable, $"local image engine failed: {e.Message}");
        }
    }
}
