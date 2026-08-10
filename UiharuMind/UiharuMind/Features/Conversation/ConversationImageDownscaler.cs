/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using SkiaSharp;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 内联给模型之前把图片缩小并重编码。只影响发出去的那一份——磁盘上的附件与界面预览始终是原图。
///
/// 两件事都要做，缺一件都白搭：
/// <list type="number">
/// <item><b>缩尺寸</b>——超过视觉模型的有效分辨率之后，再大的图既不会更清楚，也只是徒增上传体积；
/// 而框架的历史压缩把非文本内容按 <c>字节数 / 4</c> 估 token，一张大图会被估成十几万。</item>
/// <item><b>按内容选格式</b>——只缩尺寸不换格式的话，一张 1568px 的照片存成 PNG 仍有 1~2MB，
/// 是同尺寸 JPEG 的 5~10 倍。</item>
/// </list>
///
/// <b>格式不能一律用 JPEG。</b> 它的 DCT 分块与色度子采样正好打在高对比度细边缘上，
/// 也就是文字——截图里的小字、代码、UI 标签会被啃出振铃与色晕，模型认错的风险是实打实的；
/// 而截图恰恰是本应用的主场景。照片则相反，q85 几乎无损。
///
/// 分流规则用的是 PNG 自己的压缩率，不猜内容类型：<b>PNG 编完够小就直接用它</b>（无损，零风险）。
/// 截图那种大片纯色的合成图 PNG 压得极好，天然走这条；照片压不动，才落到 JPEG。
/// 这条规则同时优化了体积与质量，且不需要任何图像分类。
///
/// 用 SkiaSharp 而不是 Avalonia 的 <c>Bitmap.Save</c>：后者只写 PNG，
/// 它那个 quality 参数不改变格式（<c>Avalonia.Skia</c> 里 <c>SKEncodedImageFormat</c> 只出现一次）。
/// </summary>
public static class ConversationImageDownscaler
{
    /// <summary>
    /// 长边上限。主流视觉模型在此之上不会看得更清楚。
    /// 值住在 Core：压缩策略要按它推算图片的 token 上界（<see cref="InlineImageLimits.MaxTokensPerImage"/>），
    /// 而 Core 看不见 App，两边各写一个字面量迟早无声漂移。
    /// </summary>
    public const int MaxEdge = InlineImageLimits.MaxEdge;

    private const int JpegQuality = 85;

    /// <summary>
    /// PNG 编完不超过这个体积就直接用 PNG（无损）。
    /// 定在这里是因为再往上就压不住框架那套 <c>字节数 / 4</c> 的估算了——
    /// 150KB 约合 3.7 万 token，在 128k 模型的输入预算里还留得下余量。
    /// </summary>
    private const int LosslessLimitBytes = 150 * 1024;

    /// <summary>
    /// 按长边上限缩放并重编码为 JPEG
    /// </summary>
    /// <param name="original">原始字节</param>
    /// <param name="mediaType">原始 MIME 类型</param>
    /// <returns>要内联的字节与其 MIME 类型；处理失败或反而更大时原样返回</returns>
    public static (byte[] Bytes, string MediaType) Downscale(byte[] original, string mediaType)
    {
        if (original.Length == 0) return (original, mediaType);

        try
        {
            using SKBitmap? source = SKBitmap.Decode(original);
            if (source == null || source.Width <= 0 || source.Height <= 0) return (original, mediaType);

            (int Width, int Height) size = ComputeTargetSize(source.Width, source.Height, MaxEdge)
                                           ?? (source.Width, source.Height);

            using SKSurface surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height));
            if (surface == null) return (original, mediaType);

            // 先铺白:JPEG 没有 alpha 通道,带透明区域的截图不铺底会变成黑块
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.DrawBitmap(source, new SKRect(0, 0, size.Width, size.Height));

            using SKImage image = surface.Snapshot();
            (byte[] bytes, string type)? encoded = Encode(image);
            if (encoded == null) return (original, mediaType);

            if (encoded.Value.bytes.Length >= original.Length)
            {
                //本来就小的图重编码后反而更大,那就别换
                Log.Debug($"Image kept as-is ({original.Length:N0} bytes, " +
                          $"re-encode would be {encoded.Value.bytes.Length:N0}).");
                return (original, mediaType);
            }

            Log.Debug($"Image downscaled: {source.Width}x{source.Height} {original.Length:N0} bytes → " +
                      $"{size.Width}x{size.Height} {encoded.Value.bytes.Length:N0} bytes ({encoded.Value.type}).");
            return encoded.Value;
        }
        catch (Exception e)
        {
            Log.Warning($"Downscale image failed, sending the original: {e.Message}");
            return (original, mediaType);
        }
    }

    /// <summary>
    /// 选格式并编码：PNG 够小就无损送，压不动才退到 JPEG。
    ///
    /// 判据是 PNG 自己的压缩率而不是「这看起来像不像截图」——合成图（截图、UI、图表、
    /// 线稿）大片纯色，PNG 压得极好，天然走无损这条；而它们正是 JPEG 最会啃坏的那类。
    /// 照片压不动，落到 JPEG，那里 q85 又几乎无损。一条规则同时管住了体积与质量。
    /// </summary>
    /// <param name="image">已缩放好的图像</param>
    /// <returns>编码结果与其 MIME 类型；两种都编不出来时为 null</returns>
    private static (byte[] bytes, string type)? Encode(SKImage image)
    {
        using (SKData? png = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            if (png is { Size: > 0 } && png.Size <= LosslessLimitBytes) return (png.ToArray(), "image/png");
        }

        using SKData? jpeg = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return jpeg is { Size: > 0 } ? (jpeg.ToArray(), "image/jpeg") : null;
    }

    /// <summary>
    /// 计算缩放后的尺寸，保持宽高比
    /// </summary>
    /// <param name="width">原宽</param>
    /// <param name="height">原高</param>
    /// <param name="maxEdge">长边上限</param>
    /// <returns>目标尺寸；长边已在上限内时为 null（表示不必缩放，但仍会重编码）</returns>
    internal static (int Width, int Height)? ComputeTargetSize(int width, int height, int maxEdge)
    {
        if (width <= 0 || height <= 0 || maxEdge <= 0) return null;

        int longest = Math.Max(width, height);
        if (longest <= maxEdge) return null;

        double ratio = (double)maxEdge / longest;
        //短边至少留 1 像素:极端长宽比下四舍五入会得到 0,那样构造画布会失败
        return (Math.Max(1, (int)Math.Round(width * ratio)), Math.Max(1, (int)Math.Round(height * ratio)));
    }

    /// <summary>
    /// 从编码头判定 MIME 类型
    /// </summary>
    /// <param name="bytes">编码后的字节</param>
    /// <returns>MIME 类型；认不出为 null</returns>
    internal static string? SniffMediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";

        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }

        return null;
    }
}
