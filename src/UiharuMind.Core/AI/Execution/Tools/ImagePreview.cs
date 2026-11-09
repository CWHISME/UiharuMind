using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 视觉模型经 <c>Read</c> 看图（ADR 0053）：读到图片时缩成预览副本存进草稿目录，结果只给路径；
/// 图由 <see cref="ImagePreviewProjection"/> 在每次发送时以 user 消息带给模型——tool 消息只收文本。
///
/// 哪次调用算看图<b>只认参数里的路径</b>（<see cref="IsImageRead"/>）：Read 按扩展名分流，投影、压缩估算、
/// 工具卡缩略图按同一个扩展名判定，两边不会对不上；文本文件里恰好写着 <c>Preview:</c> 的行也进不了投影。
/// </summary>
public static class ImagePreview
{
    /// <summary>
    /// 缩图：原图字节与媒体类型 → 缩好的字节与媒体类型
    /// </summary>
    public delegate (byte[] Bytes, string MediaType) ImageDownscaler(byte[] original, string mediaType);

    /// <summary>没有缩图器时允许原样发出的上限</summary>
    internal const int MaxUnscaledBytes = 5 * 1024 * 1024;

    /// <summary>预览副本在草稿目录下的子目录</summary>
    internal static readonly string PreviewFolder = Path.Combine(AgentOutputLayout.ImagesFolder, "previews");

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"]; //与 ImageFormats.Sniff 认得的格式一致

    // 结果里每张图一行，投影与工具卡据此找图；尺寸紧跟其后一行，压缩据此计 token。写与读都在本类
    private const string PreviewPrefix = "Preview: ";
    private const string PreviewSizePrefix = "Preview size: ";

    /// <summary>缩图器。SkiaSharp 只在 App 里，由 App 启动时注入；没注入（CLI）就原样复制</summary>
    public static ImageDownscaler? Downscaler { get; set; }

    /// <summary>
    /// 路径是不是看图认的图片。Read 的分流与投影的判定都用它，只看扩展名
    /// </summary>
    /// <param name="path">文件路径（调用参数里的原样写法即可）</param>
    /// <returns>是图片扩展名为 true</returns>
    public static bool IsImagePath(string? path) =>
        !string.IsNullOrEmpty(path) && ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 这次工具调用是不是用 Read 看图。多图拼接（filePath 里用分号拼多张）也算：
    /// 拆开后任一路径是图片即为看图——拆不拆只看有没有分号，文件是否存在由 FileReader 按「存在优先」判定
    /// </summary>
    /// <param name="call">工具调用（刚产出的或从历史读回来的都行）</param>
    /// <returns>是 Read 且路径是图片为 true</returns>
    public static bool IsImageRead(FunctionCallContent call) =>
        call.Name == FileToolNames.Read
        && SplitJoinedPaths(ApprovalModeMapper.ExtractFilePath(call.Arguments)).Any(IsImagePath);

    /// <summary>按分号拆开 Read 的拼接路径（"a.png;b.png"）；无分号时原样单条</summary>
    private static IEnumerable<string> SplitJoinedPaths(string? path)
    {
        if (string.IsNullOrEmpty(path)) yield break;
        foreach (string part in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }

    /// <summary>
    /// 看一张图：缩成预览副本存进草稿目录，返回给模型的结果（路径与尺寸，图随后由投影带上）
    /// </summary>
    /// <param name="paths">路径口径（预览副本存进它的草稿目录）</param>
    /// <param name="label">模型给的路径写法，进话术</param>
    /// <param name="fullPath">图片绝对路径</param>
    /// <param name="downscaler">缩图器；null 原样复制</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="alreadyVisible">
    /// 这张图（缩好的那一份）是不是已经随本轮的用户消息附在眼前；为 true 时只回一句提示，不烘副本
    /// </param>
    /// <returns>工具结果</returns>
    internal static async Task<string> AttachAsync(AgentPathResolver paths, string label, string fullPath,
        ImageDownscaler? downscaler, CancellationToken ct, Func<byte[], bool>? alreadyVisible = null)
    {
        byte[] bytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
        if (ImageFormats.Sniff(bytes) is not { } mediaType) return NotAnImage(label);

        (byte[] preview, string previewType) = downscaler?.Invoke(bytes, mediaType) ?? (bytes, mediaType);
        if (alreadyVisible?.Invoke(preview) == true) return AlreadyAttached(label, preview);
        if (preview.Length > MaxUnscaledBytes) return $"Error: '{label}' is too large to attach.";

        string saved = await SavePreviewAsync(paths, preview, previewType, ct).ConfigureAwait(false);
        StringBuilder sb = new();
        sb.Append("Attached: ").Append(label).Append('\n');
        sb.Append(PreviewPrefix).Append(paths.ToPortable(saved)).Append('\n');
        if (ImageFormats.TryReadSize(preview, out int width, out int height))
        {
            sb.Append(PreviewSizePrefix).Append(width).Append('x').Append(height).Append('\n');
        }

        sb.Append("The image follows in the next message.");
        return sb.ToString();
    }

    /// <summary>
    /// 看不了图时的那句话：先说清是张什么图（格式、尺寸、大小），再接上该怎么办
    /// </summary>
    /// <param name="label">模型给的路径写法</param>
    /// <param name="fullPath">图片绝对路径</param>
    /// <param name="advice">接在后面的一句（指向哪个工具，或说明看不了）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>工具结果</returns>
    internal static async Task<string> DescribeAsync(string label, string fullPath, string advice, CancellationToken ct)
    {
        byte[] bytes = await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false);
        if (ImageFormats.Sniff(bytes) is not { } mediaType) return NotAnImage(label);

        string format = ImageFormats.ExtensionOf(mediaType)[1..].ToUpperInvariant();
        string size = ImageFormats.TryReadSize(bytes, out int width, out int height) ? $"{width}×{height}, " : string.Empty;
        return $"'{label}' is a {format} image ({size}{GameUtils.FormatBytes(bytes.Length)}). {advice}";
    }

    /// <summary>
    /// 从工具结果里取出预览副本的路径（草稿目录简写或绝对路径）
    /// </summary>
    /// <param name="result">工具结果原文</param>
    /// <returns>路径，按出现顺序</returns>
    public static IReadOnlyList<string> ParsePreviewPaths(string? result) => ToolResultLines.Parse(result, PreviewPrefix);

    /// <summary>
    /// 工具结果的正文：刚产出时是 string，从历史文件读回来是 <c>JsonElement{String}</c>。
    /// 投影与分叉复制都要读它，口径只留这一处
    /// </summary>
    /// <param name="result">工具结果</param>
    /// <returns>正文；没有结果时为空</returns>
    internal static string? ResultText(FunctionResultContent result) => result.Result switch
    {
        string text => text,
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => element.GetString(),
        null => null,
        var other => other.ToString(),
    };

    /// <summary>
    /// 分叉会话时把<b>保留下来的历史</b>里引用到的预览图带进新房间。
    ///
    /// <para>预览在历史里写的是 <c>$DRAFT/images/previews/&lt;hash&gt;.png</c>（相对草稿目录），
    /// 而草稿目录按会话分房间——新会话那间是空的，投影就会静默降级成
    /// <c>[Image no longer available]</c>：那条消息的字节从此与父会话不同，
    /// 前缀从那里起永久断（实测分叉会话的缓存停在第一张图之前）。</para>
    ///
    /// <para>复制而不是共享：两个房间各存一份，删掉任一方都不影响另一方（与
    /// <c>OwnedAttachmentFiles</c> 同一口径——谁的文件谁删）。</para>
    /// </summary>
    /// <param name="history">分叉后保留的历史</param>
    /// <param name="fromRoom">原会话的草稿目录</param>
    /// <param name="toRoom">新会话的草稿目录</param>
    /// <returns>实际复制了几张</returns>
    public static int CarryOverPreviews(IEnumerable<ChatMessage> history, string fromRoom, string toRoom)
    {
        if (fromRoom.Length == 0 || toRoom.Length == 0) return 0;
        if (string.Equals(fromRoom, toRoom, StringComparison.OrdinalIgnoreCase)) return 0;

        int copied = 0;
        foreach (ChatMessage message in history)
        {
            foreach (FunctionResultContent result in message.Contents.OfType<FunctionResultContent>())
            {
                foreach (string preview in ParsePreviewPaths(ResultText(result)))
                {
                    if (!TryRelativeToDraft(preview, fromRoom, out string relative)) continue;

                    string source = Path.Combine(fromRoom, relative);
                    string target = Path.Combine(toRoom, relative);
                    if (!File.Exists(source) || File.Exists(target)) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target);
                    copied++;
                }
            }
        }

        return copied;
    }

    /// <summary>预览路径 → 相对草稿目录的部分。认两种写法：<c>$DRAFT/…</c> 与落在原房间里的绝对路径</summary>
    private static bool TryRelativeToDraft(string preview, string draftRoom, out string relative)
    {
        relative = string.Empty;
        string token = "$" + AgentPathResolver.DraftVariable + "/";
        if (preview.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            relative = preview[token.Length..];
            return relative.Length > 0;
        }

        string root = Path.TrimEndingDirectorySeparator(draftRoom) + Path.DirectorySeparatorChar;
        if (!Path.IsPathRooted(preview) || !preview.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;

        relative = preview[root.Length..];
        return relative.Length > 0;
    }

    /// <summary>
    /// 结果里的一张预览：路径与尺寸
    /// </summary>
    /// <param name="Path">预览路径（草稿目录简写或绝对路径）</param>
    /// <param name="Width">宽；结果没记为 0</param>
    /// <param name="Height">高；结果没记为 0</param>
    public readonly record struct PreviewEntry(string Path, int Width, int Height);

    /// <summary>
    /// 从工具结果里取出预览与它们的尺寸
    /// </summary>
    /// <param name="result">工具结果原文</param>
    /// <returns>预览，按出现顺序</returns>
    public static IReadOnlyList<PreviewEntry> ParsePreviews(string? result)
    {
        List<PreviewEntry> previews = new();
        if (string.IsNullOrEmpty(result)) return previews;
        foreach (string line in result.Split('\n'))
        {
            if (line.StartsWith(PreviewPrefix, StringComparison.Ordinal))
            {
                previews.Add(new PreviewEntry(line[PreviewPrefix.Length..].Trim(), 0, 0));
            }
            else if (line.StartsWith(PreviewSizePrefix, StringComparison.Ordinal) && previews.Count > 0 &&
                     TryParseSize(line[PreviewSizePrefix.Length..], out int width, out int height))
            {
                previews[^1] = previews[^1] with { Width = width, Height = height };
            }
        }

        return previews;
    }

    private static string NotAnImage(string label) => $"Error: '{label}' is not a PNG, JPEG or WebP image.";

    // 图已经随用户这一轮的消息附在眼前：不再烘副本、不再多投影一份。尺寸照给——
    // 原来 Attach 会带回它，模型有时正是为它才读的（写 <img> 的宽高、算布局）
    private static string AlreadyAttached(string label, byte[] preview)
    {
        string size = ImageFormats.TryReadSize(preview, out int width, out int height) ? $" ({width}x{height})" : string.Empty;
        return $"'{label}' is already attached to the message in front of you{size}. "
               + "Look at it there instead of reading it again.";
    }

    private static bool TryParseSize(string text, out int width, out int height)
    {
        width = 0;
        height = 0;
        string[] parts = text.Trim().Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height);
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
