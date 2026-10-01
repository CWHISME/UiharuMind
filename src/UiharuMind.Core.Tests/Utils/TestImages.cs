using System.Buffers.Binary;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 只带文件头的假图：够格式识别与读宽高用，不是能解码的图
/// </summary>
internal static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        byte[] bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    public static byte[] Jpeg(int width, int height)
    {
        List<byte> bytes = [0xFF, 0xD8];
        // APP0 段：读宽高时要能跳过它
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10]);
        bytes.AddRange(new byte[14]);
        // SOF0：长度 17、精度 8、高、宽
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        bytes.AddRange([(byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);
        bytes.AddRange(new byte[12]);
        return bytes.ToArray();
    }

    public static byte[] WebpExtended(int width, int height)
    {
        byte[] bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        "VP8X"u8.CopyTo(bytes.AsSpan(12));
        WriteUInt24(bytes.AsSpan(24), width - 1);
        WriteUInt24(bytes.AsSpan(27), height - 1);
        return bytes;
    }

    private static void WriteUInt24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }
}
