using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 视觉模型自己看图（ADR 0053）。调用时把图缩成预览副本存进草稿目录，结果只给路径；
/// 图由 <see cref="ViewImageProjection"/> 在每次发送时以 user 消息带给模型——tool 消息只收文本。
/// 与 <see cref="VisionTool"/> 互斥：模型自带视觉挂这个，否则挂那个。
/// </summary>
public static class ViewImageTool
{
    /// <summary>
    /// 缩图：原图字节与媒体类型 → 缩好的字节与媒体类型
    /// </summary>
    public delegate (byte[] Bytes, string MediaType) ImageDownscaler(byte[] original, string mediaType);

    /// <summary>工具名。提示词里提到本工具时一律引用这个常量</summary>
    public const string ToolName = "ViewImage";

    /// <summary>单次调用最多几张图：每张都随之后的每次请求重发</summary>
    public const int MaxImages = 4;

    /// <summary>没有缩图器时允许原样发出的上限</summary>
    internal const int MaxUnscaledBytes = 5 * 1024 * 1024;

    /// <summary>预览副本在草稿目录下的子目录</summary>
    internal static readonly string PreviewFolder = Path.Combine(AgentOutputLayout.ImagesFolder, "previews");

    // 结果里每张图一行，投影与工具卡据此找图；写与读都在本类
    private const string PreviewPrefix = "Preview: ";

    /// <summary>缩图器。SkiaSharp 只在 App 里，由 App 启动时注入；没注入（CLI）就原样复制</summary>
    public static ImageDownscaler? Downscaler { get; set; }

    /// <summary>
    /// 创建看图 AIFunction
    /// </summary>
    /// <param name="paths">路径解析口径(与文件工具同一份)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(AgentPathResolver paths) => Create(paths, Downscaler);

    /// <summary>
    /// 创建看图 AIFunction
    /// </summary>
    /// <param name="resolver">路径解析口径</param>
    /// <param name="downscaler">缩图器；null 原样复制</param>
    /// <returns>工具实例</returns>
    internal static AITool Create(AgentPathResolver resolver, ImageDownscaler? downscaler)
    {
        return AIFunctionFactory.Create(
            async ([Description("Absolute or workspace-relative paths of the image files (at most 4).")]
                    string[] paths,
                    CancellationToken cancellationToken = default) =>
                await ViewAsync(resolver, downscaler, paths, cancellationToken)
                    .ConfigureAwait(false),
            ToolName,
            "Look at image files yourself: they are attached right after this tool's result. " +
            "Use it to check an image you found or generated. Never guess an image's content from its file name.");
    }

    /// <summary>
    /// 从工具结果里取出预览副本的路径（草稿目录简写或绝对路径）
    /// </summary>
    /// <param name="result">工具结果原文</param>
    /// <returns>路径，按调用顺序</returns>
    public static IReadOnlyList<string> ParsePreviewPaths(string? result) => ToolResultLines.Parse(result, PreviewPrefix);

    private static async Task<string> ViewAsync(AgentPathResolver resolver, ImageDownscaler? downscaler,
        string[]? imagePaths, CancellationToken ct)
    {
        if (imagePaths is null or { Length: 0 }) return "Error: paths must contain at least one path.";
        if (imagePaths.Length > MaxImages) return $"Error: at most {MaxImages} images per call.";

        // 先全部读完、缩完再落盘：任一张不行就整体报错，不留半截预览
        List<(string Path, byte[] Bytes, string MediaType)> previews = new();
        foreach (string imagePath in imagePaths)
        {
            if (!resolver.TryResolve(imagePath, out string full) || !File.Exists(full))
            {
                return $"Error: image file not found: {imagePath}";
            }

            byte[] bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
            if (ImageFormats.Sniff(bytes) is not { } mediaType) return $"Error: not a PNG, JPEG or WebP image: {imagePath}";

            (byte[] preview, string previewType) = downscaler?.Invoke(bytes, mediaType) ?? (bytes, mediaType);
            if (preview.Length > MaxUnscaledBytes) return $"Error: image too large to attach: {imagePath}";
            previews.Add((imagePath, preview, previewType));
        }

        StringBuilder sb = new();
        foreach ((string imagePath, byte[] bytes, string mediaType) in previews)
        {
            string saved = await SavePreviewAsync(resolver, bytes, mediaType, ct).ConfigureAwait(false);
            sb.Append("Attached: ").Append(imagePath).Append('\n');
            sb.Append(PreviewPrefix).Append(resolver.ToPortable(saved)).Append('\n');
        }

        sb.Append("The images follow in the next message.");
        return sb.ToString();
    }

    // 文件名取内容哈希、只写不改：投影每次读到的字节一致，前缀缓存才不失效
    private static async Task<string> SavePreviewAsync(AgentPathResolver paths, byte[] bytes, string mediaType,
        CancellationToken ct)
    {
        string directory = AgentOutputLayout.EnsureDraftSubdirectory(paths.DraftRoot, PreviewFolder);

        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
        string path = Path.Combine(directory, hash + ImageFormats.ExtensionOf(mediaType));
        if (!File.Exists(path)) await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        return path;
    }
}
