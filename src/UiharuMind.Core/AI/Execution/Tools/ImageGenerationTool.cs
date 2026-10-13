using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;
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

    // 结果里每张图一行，工具卡据此找图；写与读都在本类，格式只有一处
    private const string SavedPrefix = "Saved: ";

    private static readonly string SupportedRatios = string.Join(", ", ImageAspectRatio.Supported);

    /// <summary>
    /// 创建生图 AIFunction
    /// </summary>
    /// <param name="paths">路径解析口径(与文件工具同一份)</param>
    /// <param name="progressSink">把进度交给界面(参数为工具调用标识与进度);没有界面在看为 null</param>
    /// <returns>工具实例</returns>
    public static AITool Create(AgentPathResolver paths, Action<string, ImageProgress>? progressSink = null) =>
        Create(paths, ImageGenerationService.Shared, progressSink);

    /// <summary>
    /// 创建生图 AIFunction
    /// </summary>
    /// <param name="paths">路径解析口径</param>
    /// <param name="service">生图回退链</param>
    /// <param name="progressSink">把进度交给界面;null 不报</param>
    /// <returns>工具实例</returns>
    internal static AITool Create(AgentPathResolver paths, ImageGenerationService service,
        Action<string, ImageProgress>? progressSink = null)
    {
        // 手写 AotToolFunction（A2 全量扩展）：显式 schema + handler
        return new AotToolFunction(ToolName,
            "根据文字提示生成图像，或编辑已有图像。" +
            "结果保存为文件并展示给用户。最多需要几分钟。",
            AotToolFunction.SchemaFromNode(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["prompt"] = new JsonObject { ["type"] = "string", ["description"] = "最终图像应该长什么样。编辑时，描述期望的结果和必须保持不变的部分。" },
                    ["images"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["nullable"] = true, ["default"] = null, ["description"] = "要编辑的输入图片路径（最多 5 张）。第一张是要修改的图片，其余是参考图。省略则从零生成。" },
                    ["aspectRatio"] = new JsonObject { ["type"] = "string", ["nullable"] = true, ["default"] = null, ["description"] = "宽高比，取其一：1:1、3:4、4:3、16:9、9:16、2:3、3:2。省略则用 1:1。" },
                },
                ["required"] = new JsonArray(JsonValue.Create("prompt")),
            }),
            async (args, ct) => await GenerateAsync(paths, service,
                AotToolFunction.Marshaller.String(args, "prompt")!,
                AotToolFunction.Marshaller.StringArray(args, "images"),
                AotToolFunction.Marshaller.String(args, "aspectRatio"),
                ProgressOf(progressSink), ct).ConfigureAwait(false));
    }

    /// <summary>
    /// 从工具结果里取出保存的图片路径（草稿目录简写或绝对路径）
    /// </summary>
    /// <param name="result">工具结果原文</param>
    /// <returns>路径，按产出顺序</returns>
    public static IReadOnlyList<string> ParseSavedPaths(string? result) => ToolResultLines.Parse(result, SavedPrefix);

    // 按本次调用的标识把进度交出去;拿不到标识(不经框架直接调)就不报
    private static IProgress<ImageProgress>? ProgressOf(Action<string, ImageProgress>? sink)
    {
        string? callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
        if (sink == null || string.IsNullOrEmpty(callId)) return null;
        return new InlineProgress<ImageProgress>(p => sink(callId, p with { Preview = null }));
    }

    private static async Task<string> GenerateAsync(AgentPathResolver paths, ImageGenerationService service,
        string prompt, string[]? imagePaths, string? aspectRatio, IProgress<ImageProgress>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return "Error: 提示词不能为空。";

        ImageAspectRatio? ratio = null;
        if (!string.IsNullOrWhiteSpace(aspectRatio))
        {
            if (!ImageAspectRatio.TryParse(aspectRatio, out ImageAspectRatio parsed))
            {
                return $"Error: 不支持的宽高比 '{aspectRatio}'。请用其一：{SupportedRatios}。";
            }

            ratio = parsed;
        }

        imagePaths ??= [];
        if (imagePaths.Length > ImageRequest.MaxImages)
        {
            return $"Error: 单次最多 {ImageRequest.MaxImages} 张输入图片。";
        }

        (List<ImageInput> inputs, string? inputError) = await LoadInputsAsync(paths, imagePaths, ct).ConfigureAwait(false);
        if (inputError != null) return inputError;

        ImageGenerationReport report = await service.GenerateAsync(new ImageRequest(prompt.Trim(), inputs, ratio), progress, ct)
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

        if (missing.Count > 0) return (inputs, $"Error: 找不到图片文件：{string.Join(", ", missing)}");
        return unsupported.Count > 0
            ? (inputs, $"Error: 不是 PNG、JPEG 或 WebP 图片：{string.Join(", ", unsupported)}")
            : (inputs, null);
    }

    private static async Task<string> SaveAsync(AgentPathResolver paths, GeneratedImage image, string prompt,
        CancellationToken ct)
    {
        string directory = AgentOutputLayout.EnsureDraftSubdirectory(paths.DraftRoot, AgentOutputLayout.ImagesFolder);

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
        StringBuilder sb = new($"由 {report.ModelName} 生成。\n");
        foreach (string path in saved) sb.Append(SavedPrefix).Append(path).Append('\n');
        // 工具卡已经把图画出来了,点开就是原图:正文再贴用户就看到两份。实测「只在交付终稿时贴」挡不住,
        // 一次出图模型就当终稿;只说别贴,它又把 $DRAFT 简写当位置写进正文。所以图与路径都点名不要
        sb.Append("图片已在本工具卡片上展示给用户，点开就是原文件。不要在回复里重复它的路径。");
        if (!string.IsNullOrWhiteSpace(report.RevisedPrompt)) sb.Append("\n修订后的提示词：").Append(report.RevisedPrompt);
        AppendFallback(sb, report);
        return sb.ToString();
    }

    private static string DescribeFailure(ImageGenerationReport report)
    {
        ImageFailure failure = report.Failure!;
        StringBuilder sb = new(failure.Kind switch
        {
            EImageFailureKind.Rejected =>
                $"图像生成被 {report.ModelName} 拒绝了：{failure.Message}\n" +
                "先修订提示词或输入图片再试。",
            EImageFailureKind.MaybeCharged =>
                $"{report.ModelName} 的图像生成没有完成：{failure.Message}\n" +
                "可能已经计费，不要自行重试；告诉用户发生了什么。",
            _ => $"图像生成不可用：{failure.Message}\n" +
                 "告诉用户检查图像模型设置。",
        });
        AppendFallback(sb, report);
        return sb.ToString();
    }

    // 回退是静默的,不说的话模型会以为是它指望的那一家画的(同 ADR 0031 的回执告知)
    private static void AppendFallback(StringBuilder sb, ImageGenerationReport report)
    {
        if (report.Skipped.Count == 0 || report.Failure is { Kind: EImageFailureKind.Unavailable }) return;
        sb.Append("\n跳过这些后回退：")
            .Append(string.Join("; ", report.Skipped.Select(s => $"{s.Name} ({s.Reason})")));
    }
}
