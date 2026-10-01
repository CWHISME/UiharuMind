using System.Buffers.Binary;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 图片格式的唯一认定处：扩展名与 MIME 互推、按编码头识别、读宽高。
/// 不解码像素——Core 不引用图像库，这里只看文件头。
/// </summary>
public static class ImageFormats
{
    /// <summary>
    /// 按扩展名推 MIME 类型
    /// </summary>
    /// <param name="path">文件路径或文件名</param>
    /// <param name="fallback">认不出时的返回值</param>
    /// <returns>MIME 类型</returns>
    public static string MediaTypeFromPath(string path, string fallback)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => fallback,
        };
    }

    /// <summary>
    /// 按 MIME 类型给文件扩展名
    /// </summary>
    /// <param name="mediaType">MIME 类型</param>
    /// <returns>带点的扩展名；认不出时为 <c>.png</c></returns>
    public static string ExtensionOf(string? mediaType)
    {
        return mediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".png",
        };
    }

    /// <summary>
    /// 从编码头判定 MIME 类型
    /// </summary>
    /// <param name="bytes">编码后的字节</param>
    /// <returns>MIME 类型；认不出为 null</returns>
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (IsPng(bytes)) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (IsWebp(bytes)) return "image/webp";
        return null;
    }

    /// <summary>
    /// 读图片宽高（PNG / JPEG / WebP）
    /// </summary>
    /// <param name="bytes">编码后的字节</param>
    /// <param name="width">宽</param>
    /// <param name="height">高</param>
    /// <returns>读到返回 true</returns>
    public static bool TryReadSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (IsPng(bytes) && bytes.Length >= 24)
        {
            // IHDR 恒为第一块：宽高在偏移 16 / 20，大端
            width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..]);
            height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..]);
        }
        else if (IsWebp(bytes))
        {
            TryReadWebpSize(bytes, out width, out height);
        }
        else if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            TryReadJpegSize(bytes, out width, out height);
        }

        return width > 0 && height > 0;
    }

    /// <summary>
    /// 拼 Data URL（<c>data:{mime};base64,{data}</c>）
    /// </summary>
    /// <param name="bytes">图片字节</param>
    /// <param name="mediaType">MIME 类型</param>
    /// <returns>Data URL</returns>
    public static string ToDataUrl(byte[] bytes, string mediaType)
    {
        return $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    private static bool IsWebp(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
        bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;

    private static void TryReadWebpSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 30) return;

        ReadOnlySpan<byte> chunk = bytes.Slice(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            // 扩展格式：画布宽高各 24 位小端，存的是「减一」
            width = 1 + (bytes[24] | bytes[25] << 8 | bytes[26] << 16);
            height = 1 + (bytes[27] | bytes[28] << 8 | bytes[29] << 16);
        }
        else if (chunk.SequenceEqual("VP8 "u8))
        {
            // 有损：关键帧头之后各 14 位
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF;
        }
        else if (chunk.SequenceEqual("VP8L"u8) && bytes.Length >= 25)
        {
            // 无损：签名字节之后 14 位宽、14 位高，同样「减一」
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
            width = 1 + (int)(bits & 0x3FFF);
            height = 1 + (int)((bits >> 14) & 0x3FFF);
        }
    }

    private static void TryReadJpegSize(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        int offset = 2;
        while (offset + 9 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            byte marker = bytes[offset + 1];
            // SOF0~SOF15，除去 DHT(C4)、JPG(C8)、DAC(CC)：帧头里有宽高
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 7)..]);
                return;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 2)..]);
            if (length < 2) return;
            offset += 2 + length;
        }
    }
}
