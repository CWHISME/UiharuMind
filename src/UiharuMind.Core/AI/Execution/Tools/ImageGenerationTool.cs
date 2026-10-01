using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 生图工具：带输入图即编辑，不带即生成，背后走生图回退链（ADR 0052）。
/// 产出落进草稿目录的 <c>images/</c>，结果只给路径——<b>不把图交回模型</b>：
/// Chat Completions 的 tool 消息只收文本，交回图片等于塞一串 base64。
/// 不包审批：写入只落草稿目录，对外发图与 <see cref="VisionTool"/> 同口径。
/// </summary>
public static class ImageGenerationTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "GenerateImage";

    /// <summary>产出在草稿目录下的子目录</summary>
    public const string OutputFolder = "images";

    // 结果里每张图一行，工具卡据此找图；写与读都在本类，格式只有一处
    private const string SavedPrefix = "Saved: ";

    private static readonly string SupportedRatios = string.Join(", ", ImageAspectRatio.Supported);

    /// <summary>
    /// 创建生图 AIFunction
    /// </summary>
    /// <param name="paths">路径解析口径(与文件工具同一份)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(AgentPathResolver paths) => Create(paths, ImageGenerationService.Shared);

    /// <summary>
    /// 创建生图 AIFunction
    /// </summary>
    /// <param name="paths">路径解析口径</param>
    /// <param name="service">生图回退链</param>
    /// <returns>工具实例</returns>
    internal static AITool Create(AgentPathResolver paths, ImageGenerationService service)
    {
        return AIFunctionFactory.Create(
            async ([Description("What the final image should look like. When editing, describe the desired " +
                                "result and what must stay unchanged.")]
                    string prompt,
                    [Description("Paths of input images to edit (at most 5). The first is " +
                                 "the image to modify, the rest are references. To refine an earlier result, " +
                                 "pass its saved path. Omit to generate from scratch.")]
                    string[]? images = null,
                    [Description("Aspect ratio, one of: 1:1, 3:4, 4:3, 16:9, 9:16, 2:3, 3:2. Omit for 1:1, " +
                                 "or to keep the first input image's shape when editing.")]
                    string? aspectRatio = null,
                    CancellationToken cancellationToken = default) =>
                await GenerateAsync(paths, service, prompt, images, aspectRatio, cancellationToken)
                    .ConfigureAwait(false),
            ToolName,
            "Generate an image from a text prompt, or edit existing images. " +
            "The result is saved as a file and shown to the user. Takes up to a few minutes.");
    }

    /// <summary>
    /// 从工具结果里取出保存的图片路径（草稿目录简写或绝对路径）
    /// </summary>
    /// <param name="result">工具结果原文</param>
    /// <returns>路径，按产出顺序</returns>
    public static IReadOnlyList<string> ParseSavedPaths(string? result)
    {
        if (string.IsNullOrEmpty(result)) return [];
        return result.Split('\n')
            .Where(line => line.StartsWith(SavedPrefix, StringComparison.Ordinal))
            .Select(line => line[SavedPrefix.Length..].Trim())
            .ToList();
    }

    private static async Task<string> GenerateAsync(AgentPathResolver paths, ImageGenerationService service,
        string prompt, string[]? imagePaths, string? aspectRatio, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return "Error: prompt must not be empty.";

        ImageAspectRatio? ratio = null;
        if (!string.IsNullOrWhiteSpace(aspectRatio))
        {
            if (!ImageAspectRatio.TryParse(aspectRatio, out ImageAspectRatio parsed))
            {
                return $"Error: unsupported aspectRatio '{aspectRatio}'. Use one of: {SupportedRatios}.";
            }

            ratio = parsed;
        }

        imagePaths ??= [];
        if (imagePaths.Length > ImageRequest.MaxImages)
        {
            return $"Error: at most {ImageRequest.MaxImages} input images per call.";
        }

        (List<ImageInput> inputs, string? inputError) = await LoadInputsAsync(paths, imagePaths, ct).ConfigureAwait(false);
        if (inputError != null) return inputError;

        ImageGenerationReport report = await service.GenerateAsync(new ImageRequest(prompt.Trim(), inputs, ratio), ct)
            .ConfigureAwait(false);
        if (report.Failure != null) return DescribeFailure(report);

        List<string> saved = new();
        foreach (GeneratedImage image in report.Images)
        {
            string file = await SaveAsync(paths, image, prompt, ct).ConfigureAwait(false);
            saved.Add(paths.ToPortable(file));
        }

        return DescribeSuccess(report, saved);
    }

    // 与 AnalyzeImage 同一口径:任一缺失或认不出就整体报错并列全,不搞「缺的跳过、剩的照做」
    private static async Task<(List<ImageInput> Inputs, string? Error)> LoadInputsAsync(AgentPathResolver paths,
        string[] imagePaths, CancellationToken ct)
    {
        List<ImageInput> inputs = new();
        List<string> missing = new();
        List<string> unsupported = new();
        foreach (string imagePath in imagePaths)
        {
            if (!paths.TryResolve(imagePath, out string full) || !File.Exists(full))
            {
                missing.Add(imagePath);
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
            if (ImageFormats.Sniff(bytes) is { } mediaType) inputs.Add(new ImageInput(bytes, mediaType));
            else unsupported.Add(imagePath);
        }

        if (missing.Count > 0) return (inputs, $"Error: image file not found: {string.Join(", ", missing)}");
        return unsupported.Count > 0
            ? (inputs, $"Error: not a PNG, JPEG or WebP image: {string.Join(", ", unsupported)}")
            : (inputs, null);
    }

    private static async Task<string> SaveAsync(AgentPathResolver paths, GeneratedImage image, string prompt,
        CancellationToken ct)
    {
        // 能力预览没有会话、没有草稿目录,那条路不跑轮次;兜底到产出根免得写进进程工作目录
        string root = paths.DraftRoot.Length > 0 ? paths.DraftRoot : AgentOutputLayout.RootPath;
        string directory = Path.Combine(root, OutputFolder);
        Directory.CreateDirectory(directory);

        string stem = BuildFileStem(prompt, DateTime.Now);
        string extension = ImageFormats.ExtensionOf(image.MediaType);
        string path = Path.Combine(directory, stem + extension);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(directory, $"{stem}-{i}{extension}");

        await File.WriteAllBytesAsync(path, image.Bytes, ct).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// 文件名主干：时间戳 + 提示词里的英文词。中文提示词取不出词，就只剩时间戳
    /// </summary>
    /// <param name="prompt">提示词</param>
    /// <param name="now">当前时间</param>
    /// <returns>不含扩展名的文件名</returns>
    internal static string BuildFileStem(string prompt, DateTime now)
    {
        const int maxSlugLength = 40;
        StringBuilder slug = new();
        bool pendingSeparator = false;
        foreach (char c in prompt.ToLowerInvariant())
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                pendingSeparator = slug.Length > 0;
                continue;
            }

            if (slug.Length + (pendingSeparator ? 2 : 1) > maxSlugLength) break;
            if (pendingSeparator) slug.Append('-');
            slug.Append(c);
            pendingSeparator = false;
        }

        string stamp = now.ToString("yyyyMMdd-HHmmss");
        return slug.Length > 0 ? $"{stamp}-{slug}" : stamp;
    }

    private static string DescribeSuccess(ImageGenerationReport report, List<string> saved)
    {
        StringBuilder sb = new($"Generated by {report.ModelName}.\n");
        foreach (string path in saved) sb.Append(SavedPrefix).Append(path).Append('\n');
        // 工具卡已经把图画出来了:每版草稿都往正文里贴一遍,用户会看到两份
        sb.Append("The image is already shown to the user; embed it in your reply only when presenting a final result.");
        if (!string.IsNullOrWhiteSpace(report.RevisedPrompt)) sb.Append("\nRevised prompt: ").Append(report.RevisedPrompt);
        AppendFallback(sb, report);
        return sb.ToString();
    }

    private static string DescribeFailure(ImageGenerationReport report)
    {
        ImageFailure failure = report.Failure!;
        StringBuilder sb = new(failure.Kind switch
        {
            EImageFailureKind.Rejected =>
                $"Image generation was rejected by {report.ModelName}: {failure.Message}\n" +
                "Revise the prompt or the input images before trying again.",
            EImageFailureKind.MaybeCharged =>
                $"Image generation by {report.ModelName} did not complete: {failure.Message}\n" +
                "It may already have been billed, so do not retry on your own; tell the user what happened.",
            _ => $"Image generation is unavailable: {failure.Message}\n" +
                 "Tell the user to check the image model settings.",
        });
        AppendFallback(sb, report);
        return sb.ToString();
    }

    // 回退是静默的,不说的话模型会以为是它指望的那一家画的(同 ADR 0031 的回执告知)
    private static void AppendFallback(StringBuilder sb, ImageGenerationReport report)
    {
        if (report.Skipped.Count == 0 || report.Failure is { Kind: EImageFailureKind.Unavailable }) return;
        sb.Append("\nFell back after skipping: ")
            .Append(string.Join("; ", report.Skipped.Select(s => $"{s.Name} ({s.Reason})")));
    }
}
