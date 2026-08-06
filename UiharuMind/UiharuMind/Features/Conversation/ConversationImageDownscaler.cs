/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 内联给模型之前把图片缩到合理尺寸。只影响发出去的那一份——磁盘上的附件与界面预览始终是原图。
///
/// 两个理由，第二个更硬：
/// 一是超过视觉模型的有效分辨率之后，再大的图既不会更清楚，也只是徒增 token 与上传体积；
/// 二是框架的历史压缩把非文本内容按 <c>字节数 / 4</c> 估算 token，一张 3MB 截图会被估成
/// 七十多万 token，不缩放的话带图会话一进来就会触发压缩。
/// </summary>
public static class ConversationImageDownscaler
{
    /// <summary>长边上限。主流视觉模型在此之上不会看得更清楚</summary>
    public const int MaxEdge = 1568;

    private const int EncodeQuality = 85;

    /// <summary>
    /// 按长边上限缩放并重编码
    /// </summary>
    /// <param name="original">原始字节</param>
    /// <param name="mediaType">原始 MIME 类型</param>
    /// <returns>要内联的字节与其 MIME 类型；无需缩放或处理失败时原样返回</returns>
    public static (byte[] Bytes, string MediaType) Downscale(byte[] original, string mediaType)
    {
        if (original.Length == 0) return (original, mediaType);

        try
        {
            using MemoryStream source = new(original);
            using Bitmap bitmap = new(source);

            (int Width, int Height)? target = ComputeTargetSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height, MaxEdge);
            if (target == null) return (original, mediaType);

            using Bitmap scaled = bitmap.CreateScaledBitmap(
                new PixelSize(target.Value.Width, target.Value.Height), BitmapInterpolationMode.HighQuality);
            using MemoryStream output = new();
            scaled.Save(output, EncodeQuality);
            byte[] bytes = output.ToArray();

            // 重编码后反而更大就作废(本来就小的 JPEG 被编成 PNG 会这样)
            if (bytes.Length == 0 || bytes.Length >= original.Length) return (original, mediaType);

            // 编码格式由 Avalonia 后端决定,不能想当然当成 JPEG:MIME 标错会被模型接口拒收
            return (bytes, SniffMediaType(bytes) ?? mediaType);
        }
        catch (Exception e)
        {
            Log.Warning($"Downscale image failed, sending the original: {e.Message}");
            return (original, mediaType);
        }
    }

    /// <summary>
    /// 计算缩放后的尺寸，保持宽高比
    /// </summary>
    /// <param name="width">原宽</param>
    /// <param name="height">原高</param>
    /// <param name="maxEdge">长边上限</param>
    /// <returns>目标尺寸；长边已在上限内时为 null（表示不必缩放）</returns>
    internal static (int Width, int Height)? ComputeTargetSize(int width, int height, int maxEdge)
    {
        if (width <= 0 || height <= 0 || maxEdge <= 0) return null;

        int longest = Math.Max(width, height);
        if (longest <= maxEdge) return null;

        double ratio = (double)maxEdge / longest;
        //短边至少留 1 像素:极端长宽比下四舍五入会得到 0,那样构造 PixelSize 会抛
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
