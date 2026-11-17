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

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// 内联给模型之前把图片缩小并重编码。只影响发出去的那一份——磁盘上的附件与界面预览始终是原图。
///
/// 两件事都要做，缺一件都白搭：
/// <list type="number">
/// <item><b>缩尺寸</b>——超过视觉模型的有效分辨率之后，再大的图既不会更清楚，也只是徒增上传体积。
/// 而图片是随历史每一轮重传的，省下的不是一次，是每一次。</item>
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
    /// 长边上限。值住在 Core：压缩策略要按 <see cref="InlineImageLimits.MaxEdge"/> 推算图片的 token 上界，
    /// 而 Core 看不见 App——两边各写一个字面量会无声漂移。
    ///
    /// <b>缩放按 <see cref="InlineImageLimits.SendEdge"/>，计价按 <see cref="InlineImageLimits.MaxEdge"/></b>：
    /// 前者是「能吃满像素的那一家」（DeepSeek 归一化到 1.69M 总像素），后者是「最贵的那一家」
    /// （Anthropic 超过 1568 自己缩）。多出来的像素不多花 token，只多花一点上传
    /// </summary>
    public const int MaxEdge = InlineImageLimits.SendEdge;

    /// <summary>
    /// 单张内联图片的体积预算。PNG 编完不超过它就直接无损送，超了才退到 JPEG，
    /// 而 JPEG 也要逐档降质压进这个数以内。
    ///
    /// 这个上限原本是为了压住框架那套 <c>字节数 / 4</c> 的 token 估算，如今那条已由
    /// <see cref="InlineImageLimits.MaxTokensPerImage"/> 从源头修掉；150KB 那一档的理由（上传带宽）
    /// 也交回给总量预算（<c>ImageBudget</c>）去管。既然带宽有人兜底，这一档就该按<b>画质</b>定：
    /// 实测 344 次缩图里 342 次落到 JPEG，43 次在 q55 之下仍超 150KB 照发——线压得太紧，
    /// 截图上的小字正好是 JPEG 最会啃坏的那类（q55 的振铃与色晕）。
    /// 400KB 让多数截图回到 PNG 或至少 q85，同时仍远低于总量预算的触发线
    /// </summary>
    internal const int MaxInlineBytes = 400 * 1024;

    /// <summary>
    /// JPEG 的降质档位。逐档往下试直到进预算：多数图在 q85 就停下了，只有本来就压不动的大照片
    /// 才会往下走。最低档仍超预算时按最低档发出去（并记 Warning）：图大总好过发不出图
    /// </summary>
    private static readonly int[] JpegQualitySteps = [85, 70, 55];

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
            using (SKImage sourceImage = SKImage.FromBitmap(source))
                surface.Canvas.DrawImage(sourceImage, new SKRect(0, 0, size.Width, size.Height), SKSamplingOptions.Default);

            using SKImage image = surface.Snapshot();
            (byte[] bytes, string type, int? quality)? encoded = Encode(image);
            if (encoded == null) return (original, mediaType);

            if (encoded.Value.bytes.Length >= original.Length)
            {
                //本来就小的图重编码后反而更大,那就别换
                Log.Debug($"Image kept as-is ({original.Length:N0} bytes, " +
                          $"re-encode would be {encoded.Value.bytes.Length:N0}).");
                return (original, mediaType);
            }

            // 档位要记:只记格式的话,「这张图是不是被 q55 啃过」永远只能靠 Image still 那行反推
            string quality = encoded.Value.quality is { } level ? $" q{level}" : string.Empty;
            Log.Debug($"Image downscaled: {source.Width}x{source.Height} {original.Length:N0} bytes → " +
                      $"{size.Width}x{size.Height} {encoded.Value.bytes.Length:N0} bytes " +
                      $"({encoded.Value.type}{quality}).");
            return (encoded.Value.bytes, encoded.Value.type);
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
    ///
    /// <b>注意 PNG 那条在真实素材上很少成立</b>（实测 344 次缩图只有 2 次落成 PNG）：这里的截图
    /// 多是游戏画面与带渐变的 UI，1568~1792 见方编出来普遍超过 <see cref="MaxInlineBytes"/>。
    /// 所以决定画质的主要是 JPEG 从哪一档停下，档位要跟着日志走
    /// </summary>
    /// <param name="image">已缩放好的图像</param>
    /// <returns>编码结果、MIME 类型与 JPEG 质量档（PNG 为 null）；两种都编不出来时为 null</returns>
    private static (byte[] bytes, string type, int? quality)? Encode(SKImage image)
    {
        using (SKData? png = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            if (png is { Size: > 0 } && png.Size <= MaxInlineBytes) return (png.ToArray(), "image/png", null);
        }

        // 档位从高到低,所以最后拿到的那份也是最小的一份
        byte[]? smallest = null;
        int smallestQuality = 0;
        foreach (int quality in JpegQualitySteps)
        {
            using SKData? jpeg = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (jpeg is not { Size: > 0 }) continue;
            if (jpeg.Size <= MaxInlineBytes) return (jpeg.ToArray(), "image/jpeg", quality);
            smallest = jpeg.ToArray();
            smallestQuality = quality;
        }

        if (smallest == null) return null;

        // 照发,但它是在悄悄放大请求体——总量预算按字节裁,这一截就是裁不准的来源,得留痕
        Log.Warning($"Image still {smallest.Length:N0} bytes at q{smallestQuality} " +
                    $"(budget {MaxInlineBytes:N0}); sending it anyway.");
        return (smallest, "image/jpeg", smallestQuality);
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
}
