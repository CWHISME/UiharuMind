using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 生图工具：带输入图即编辑，不带即生成，背后走生图回退链（ADR 0052）。
/// 点名 <c>models</c> 时只试那几家、每家各出一张，不回退。
/// 产出落进草稿目录的 <c>images/</c>，结果只给路径——<b>不把图交回模型</b>：
/// Chat Completions 的 tool 消息只收文本，交回图片等于塞一串 base64。
/// 不包审批：写入只落草稿目录，对外发图与 <see cref="VisionTool"/> 同口径。
/// </summary>
public static class ImageGenerationTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "GenerateImage";

    /// <summary>单次最多点名几家：每一家都可能计费又都慢，四家足够对比，多了只会让用户白等</summary>
    public const int MaxModels = 4;

    // 结果里每张图一行，工具卡据此找图；写与读都在本类，格式只有一处
    private const string SavedPrefix = "Saved: ";

    // 结果收尾那句：一家出一段图、最后统一说一遍。工具卡已经把图画出来了，点开就是原图，正文再贴用户就看到两份；
    // 实测「只在交付终稿时贴」挡不住，一次出图模型就当终稿；只说别贴，它又把 $DRAFT 简写当位置写进正文。所以图与路径都点名不要
    private const string NoRepeatNote = "图片已在本工具卡片上展示给用户，点开就是原文件。不要在回复里重复它的路径。";

    private static readonly string SupportedRatios = string.Join(", ", ImageAspectRatio.Supported);

    private static readonly string ImageModelsMention =
        BuiltinExtensions.Mention(BuiltinExtensions.Image, BuiltinExtensions.ImageModels);

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
        return ToolJson.CreateFunction(
            async ([Description("最终图像应该长什么样。编辑时，描述期望的结果和必须保持不变的部分。")]
                    string prompt,
                    [Description("要编辑的输入图片路径（最多 5 张）。第一张是要修改的图片，其余是参考图。" +
                                 "要精修先前的结果，传它的保存路径。省略则从零生成。")]
                    string[]? images = null,
                    [Description("宽高比，取其一：1:1、3:4、4:3、16:9、9:16、2:3、3:2。省略则用 1:1，" +
                                 "或编辑时保持第一张输入图的形状。")]
                    string? aspectRatio = null,
                    [Description("要用哪几家生图，写模型名（最多 4 个，名字先用 image.models 查）。" +
                                 "省略则按登记顺序自动挑一家、用不了就换下一家；点名了就只试这几家、各出一张，不再换家。")]
                    string[]? models = null,
                    CancellationToken cancellationToken = default) =>
                await GenerateAsync(paths, service, prompt, images, aspectRatio, models, ProgressOf(progressSink),
                        cancellationToken)
                    .ConfigureAwait(false),
            ToolName,
            "根据文字提示生成图像，或编辑已有图像。" +
            "结果保存为文件并展示给用户。最多需要几分钟。" +
            $"想选具体的生图模型（可多选、各出一张）就先查 {ImageModelsMention}，把名字填进 models。");
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
        string prompt, string[]? imagePaths, string? aspectRatio, string[]? modelNames,
        IProgress<ImageProgress>? progress, CancellationToken ct)
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

        ImageRequest request = new(prompt.Trim(), inputs, ratio);
        return modelNames is { Length: > 0 }
            ? await GenerateNamedAsync(paths, service, request, modelNames, progress, ct).ConfigureAwait(false)
            : await GenerateFallbackAsync(paths, service, request, progress, ct).ConfigureAwait(false);
    }

    // 不点名：交给回退链自己挑一家（ADR 0052）
    private static async Task<string> GenerateFallbackAsync(AgentPathResolver paths, ImageGenerationService service,
        ImageRequest request, IProgress<ImageProgress>? progress, CancellationToken ct)
    {
        ImageGenerationReport report = await service.GenerateAsync(request, progress, ct).ConfigureAwait(false);
        if (report.Failure != null) return DescribeFailure(report);

        return DescribeProduced(report, await SaveAllAsync(paths, report, request.Prompt, ct).ConfigureAwait(false))
            .Append(NoRepeatNote).ToString();
    }

    // 点名了：只试点的这几家，每家各出一张，不回退（画图窗口里用户选了谁就是谁，同一条语义）。
    // 一家挂了不牵连别家：它们各自计费、各自失败，结果一并交回
    private static async Task<string> GenerateNamedAsync(AgentPathResolver paths, ImageGenerationService service,
        ImageRequest request, string[] modelNames, IProgress<ImageProgress>? progress, CancellationToken ct)
    {
        (List<ImageModelInfo> chosen, string? error) = ResolveNamedRequest(service, modelNames);
        if (error != null) return error;

        StringBuilder sb = new();
        List<string> failures = new();
        foreach (ImageModelInfo model in chosen)
        {
            ImageGenerationReport report = await service.GenerateWithAsync(model, request, progress, ct)
                .ConfigureAwait(false);
            if (report.Failure != null)
            {
                failures.Add(DescribeFailure(report));
                continue;
            }

            sb.Append(DescribeProduced(report, await SaveAllAsync(paths, report, request.Prompt, ct).ConfigureAwait(false)));
        }

        // 全挂：原因就是全部结果，不再多一句「图已展示」
        if (sb.Length == 0) return string.Join("\n", failures);

        sb.Append(NoRepeatNote);
        if (failures.Count > 0) sb.Append("\n这几个没出成：\n").Append(string.Join('\n', failures));
        return sb.ToString();
    }

    /// <summary>
    /// 点名清单拆成具体模型：名字重复、不认识的都在这里一次说清，不让模型猜
    /// </summary>
    /// <param name="service">生图回退链</param>
    /// <param name="modelNames">模型写的名字</param>
    /// <returns>解好的模型；出错时为空，报错在 Error 里</returns>
    private static (List<ImageModelInfo> Models, string? Error) ResolveNamedRequest(ImageGenerationService service,
        string[] modelNames)
    {
        string[] names = modelNames.Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length > MaxModels)
            return ([], $"Error: 一次最多点名 {MaxModels} 个生图模型，这次给了 {names.Length} 个。");

        List<ImageModelInfo> models = new();
        List<string> unknown = new();
        foreach (string name in names)
        {
            if (service.Find(name) is not { } model) unknown.Add(name);
            else if (!models.Contains(model)) models.Add(model);
        }

        if (unknown.Count == 0) return (models, null);

        string available = string.Join("、", service.Models.Select(x => x.Name));
        return ([], $"Error: 没有这些生图模型：{string.Join("、", unknown)}。" +
                     $"现有的：{(available.Length > 0 ? available : "（一个都没配）")}。用 {ImageModelsMention} 查名字。");
    }

    private static async Task<List<string>> SaveAllAsync(AgentPathResolver paths, ImageGenerationReport report,
        string prompt, CancellationToken ct)
    {
        List<string> saved = new();
        foreach (GeneratedImage image in report.Images)
        {
            string file = await SaveAsync(paths, image, prompt, ct).ConfigureAwait(false);
            saved.Add(paths.ToPortable(file));
        }

        return saved;
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

    // 一家出图的那一段，不含收尾那句：点名多家时它属于整条结果，只出现一次
    private static StringBuilder DescribeProduced(ImageGenerationReport report, List<string> saved)
    {
        StringBuilder sb = new($"由 {report.ModelName} 生成。\n");
        foreach (string path in saved) sb.Append(SavedPrefix).Append(path).Append('\n');
        if (!string.IsNullOrWhiteSpace(report.RevisedPrompt)) sb.Append("修订后的提示词：").Append(report.RevisedPrompt).Append('\n');
        AppendFallback(sb, report);
        return sb;
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
