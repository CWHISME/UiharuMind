using UiharuMind.Core.AI.ImageGeneration.Dialects;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 生图回退链：按登记顺序问，<b>只在「这一家现在用不了」时换下一个</b>；请求本身不行与可能已扣费
/// 一律停下交还（ADR 0052）。返回的 URL 在这里统一下载——SenseNova 的链接只活 24 小时。
/// 不碰 UI、不碰 harness：落盘到哪由调用方决定。
/// </summary>
public sealed class ImageGenerationService
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(2);

    private readonly Func<IReadOnlyList<ImageModelInfo>> _models;
    private readonly Func<EImageDialect, IImageDialect> _dialects;
    private readonly Func<string, CancellationToken, Task<byte[]>> _download;

    /// <summary>读全局生图模型列表的那一条</summary>
    public static ImageGenerationService Shared { get; } =
        new(() => ImageModelSettingConfig.Current.Models, ImageDialects.For, DownloadAsync);

    /// <param name="models">生图模型列表来源，每次调用现取</param>
    /// <param name="dialects">接口格式 → 适配器</param>
    /// <param name="download">下载结果图</param>
    internal ImageGenerationService(Func<IReadOnlyList<ImageModelInfo>> models,
        Func<EImageDialect, IImageDialect> dialects, Func<string, CancellationToken, Task<byte[]>> download)
    {
        _models = models;
        _dialects = dialects;
        _download = download;
    }

    /// <summary>至少配了一个生图模型。只看配置不看连通——装配期据此决定挂不挂工具</summary>
    public bool HasConfiguredModel => _models().Any(m => m.IsConfigured);

    /// <summary>
    /// 一个生图模型此刻的状态，设置页的列表据此显示
    /// </summary>
    /// <param name="model">生图模型</param>
    /// <returns>状态与最近一次失败原因</returns>
    public static ImageModelStatus StatusOf(ImageModelInfo model)
    {
        if (!model.IsConfigured) return new ImageModelStatus(EImageModelState.NotConfigured, null);

        string key = CircuitKey(model);
        return ServiceCircuit.IsTripped(key, out _)
            ? new ImageModelStatus(EImageModelState.Cooling, ServiceCircuit.GetLastError(key))
            : new ImageModelStatus(EImageModelState.Ready, ServiceCircuit.GetLastError(key));
    }

    /// <summary>
    /// 出一次图
    /// </summary>
    /// <param name="request">出图请求</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>结果，失败不抛异常</returns>
    public async Task<ImageGenerationReport> GenerateAsync(ImageRequest request, CancellationToken ct)
    {
        List<SkippedImageModel> skipped = new();
        List<string> ineligible = new();
        foreach (ImageModelInfo model in _models().ToList())
        {
            if (IneligibleReason(model, request) is { } reason)
            {
                ineligible.Add($"{model.Name} ({reason})");
                continue;
            }

            string circuitKey = CircuitKey(model);
            if (ServiceCircuit.IsTripped(circuitKey, out TimeSpan cooldown))
            {
                skipped.Add(new SkippedImageModel(model.Name,
                    $"disabled for {cooldown.TotalSeconds:F0}s after repeated failures"));
                continue;
            }

            ImageOutcome outcome = await _dialects(model.Dialect).GenerateAsync(model, request, ct)
                .ConfigureAwait(false);
            if (outcome.Failure is { Kind: EImageFailureKind.Unavailable } unavailable)
            {
                ServiceCircuit.RecordFailure(circuitKey, unavailable.Message);
                skipped.Add(new SkippedImageModel(model.Name, unavailable.Message));
                Log.Warning($"[ImageGen] '{model.Name}' unavailable, falling back: {unavailable.Message}");
                continue;
            }

            if (outcome.Failure != null)
            {
                Log.Warning($"[ImageGen] '{model.Name}' {outcome.Failure.Kind}: {outcome.Failure.Message}");
                return Failed(model.Name, outcome.Failure, skipped);
            }

            ServiceCircuit.RecordSuccess(circuitKey);
            return await MaterializeAsync(model.Name, outcome, skipped, ct).ConfigureAwait(false);
        }

        string tried = string.Join("; ", skipped.Select(s => $"{s.Name} ({s.Reason})").Concat(ineligible));
        string message = tried.Length == 0 ? "no image model is configured" : $"no image model available: {tried}";
        return Failed(null, new ImageFailure(EImageFailureKind.Unavailable, message), skipped);
    }

    // 不算回退的跳过：这一家压根不该接这一单
    private static string? IneligibleReason(ImageModelInfo model, ImageRequest request)
    {
        if (!model.IsConfigured) return "not configured";
        return request.IsEdit && !model.SupportsEditing ? "does not support editing" : null;
    }

    private async Task<ImageGenerationReport> MaterializeAsync(string modelName, ImageOutcome outcome,
        List<SkippedImageModel> skipped, CancellationToken ct)
    {
        List<GeneratedImage> images = new();
        foreach (ImageOutput output in outcome.Images)
        {
            byte[] bytes;
            try
            {
                bytes = output.Bytes ?? await FetchAsync(output.Url!, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // 图已出、费已扣，只是没下到：把临时链接交出去，用户还能自己去取
                return Failed(modelName, new ImageFailure(EImageFailureKind.MaybeCharged,
                    $"the image was generated but downloading it failed ({e.Message}); temporary link: {output.Url}"),
                    skipped);
            }

            if (ImageFormats.Sniff(bytes) is not { } mediaType)
            {
                return Failed(modelName, new ImageFailure(EImageFailureKind.MaybeCharged,
                    "the service returned data that is not a PNG, JPEG or WebP image"), skipped);
            }

            images.Add(new GeneratedImage(bytes, mediaType));
        }

        Log.Debug($"[ImageGen] '{modelName}' produced {images.Count} image(s)");
        return new ImageGenerationReport
        {
            ModelName = modelName, Images = images, RevisedPrompt = outcome.RevisedPrompt, Skipped = skipped,
        };
    }

    // 有的网关把 base64 塞在 url 字段里交回来
    private Task<byte[]> FetchAsync(string url, CancellationToken ct)
    {
        const string marker = ";base64,";
        int index = url.IndexOf(marker, StringComparison.Ordinal);
        return url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && index > 0
            ? Task.FromResult(Convert.FromBase64String(url[(index + marker.Length)..]))
            : _download(url, ct);
    }

    private static ImageGenerationReport Failed(string? modelName, ImageFailure failure,
        IReadOnlyList<SkippedImageModel> skipped) =>
        new() { ModelName = modelName, Failure = failure, Skipped = skipped };

    // 与 websearch 共用熔断记账，加前缀免得撞名
    private static string CircuitKey(ImageModelInfo model) => $"Image:{model.Name}";

    private static async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DownloadTimeout);
        return await ImageDialects.Http.GetByteArrayAsync(url, timeout.Token).ConfigureAwait(false);
    }
}
