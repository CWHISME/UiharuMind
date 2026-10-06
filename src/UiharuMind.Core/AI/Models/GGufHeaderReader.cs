using System.Globalization;
using System.Text;

namespace UiharuMind.Core.AI.Models;

/// <summary>
/// GGUF 文件头：键值元数据与张量统计。数组类型的键不保留（与 llama.cpp 的模型元数据口径一致）。
/// </summary>
public sealed class GGufHeader
{
    /// <summary>
    /// GGUF 格式版本
    /// </summary>
    public uint Version { get; init; }

    /// <summary>
    /// 非数组键值，值为原始类型的装箱（整数、浮点、bool、string）
    /// </summary>
    public IReadOnlyDictionary<string, object> Values { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// 张量个数
    /// </summary>
    public ulong TensorCount { get; init; }

    /// <summary>
    /// 全部张量的元素数之和
    /// </summary>
    public ulong ParameterCount { get; init; }

    /// <summary>
    /// 取字符串值，不存在或类型不符返回空串
    /// </summary>
    public string GetString(string key)
    {
        return Values.TryGetValue(key, out object? value) && value is string text ? text : "";
    }

    /// <summary>
    /// 取整数值，不存在或不是整数返回 null
    /// </summary>
    public long? GetInteger(string key)
    {
        if (!Values.TryGetValue(key, out object? value)) return null;
        return value switch
        {
            byte v => v,
            sbyte v => v,
            ushort v => v,
            short v => v,
            uint v => v,
            int v => v,
            ulong v => v > long.MaxValue ? null : (long)v,
            long v => v,
            _ => null
        };
    }

    /// <summary>
    /// 全部键值转成字符串，供展示与缓存
    /// </summary>
    public Dictionary<string, string> ToStringDictionary()
    {
        Dictionary<string, string> result = new(Values.Count);
        foreach ((string key, object value) in Values)
        {
            result[key] = value switch
            {
                bool flag => flag ? "true" : "false",
                // 与 llama.cpp 的 %f 输出一致，旧缓存里的值不会因换解析器而变
                float number => number.ToString("F6", CultureInfo.InvariantCulture),
                double number => number.ToString("F6", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
        }

        return result;
    }
}

/// <summary>
/// 纯托管的 GGUF 头部解析：只读键值段与张量信息段，不碰张量数据。支持 v2/v3（小端）。
/// </summary>
public static class GGufHeaderReader
{
    private const uint Magic = 0x46554747; // "GGUF" 小端
    private const ulong MaxStringBytes = 1 << 20;
    private const ulong MaxArrayCount = 1 << 24;
    private const ulong MaxEntryCount = 1 << 20;
    private const uint MaxTensorDims = 4;

    private enum EValueType : uint
    {
        UInt8 = 0,
        Int8 = 1,
        UInt16 = 2,
        Int16 = 3,
        UInt32 = 4,
        Int32 = 5,
        Float32 = 6,
        Bool = 7,
        String = 8,
        Array = 9,
        UInt64 = 10,
        Int64 = 11,
        Float64 = 12
    }

    /// <summary>
    /// 读取文件头
    /// </summary>
    /// <param name="path">gguf 文件路径</param>
    /// <returns>解析结果</returns>
    /// <exception cref="InvalidDataException">不是合法 GGUF 或字段越界</exception>
    public static GGufHeader Read(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return Read(stream);
    }

    /// <summary>
    /// 从流读取文件头，流须位于文件开头
    /// </summary>
    /// <param name="stream">数据流</param>
    /// <returns>解析结果</returns>
    /// <exception cref="InvalidDataException">不是合法 GGUF 或字段越界</exception>
    public static GGufHeader Read(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Not a GGUF file.");
            uint version = reader.ReadUInt32();
            if (version is < 2 or > 3) throw new InvalidDataException($"Unsupported GGUF version {version}.");

            ulong tensorCount = ReadCount(reader, MaxEntryCount);
            ulong kvCount = ReadCount(reader, MaxEntryCount);

            Dictionary<string, object> values = new((int)kvCount);
            for (ulong i = 0; i < kvCount; i++)
            {
                string key = ReadString(reader);
                EValueType type = (EValueType)reader.ReadUInt32();
                if (type == EValueType.Array)
                {
                    SkipArray(reader);
                    continue;
                }

                values[key] = ReadScalar(reader, type);
            }

            ulong parameterCount = 0;
            for (ulong i = 0; i < tensorCount; i++)
            {
                SkipString(reader);
                uint dims = reader.ReadUInt32();
                if (dims > MaxTensorDims) throw new InvalidDataException($"Tensor has {dims} dims.");
                ulong elements = 1;
                for (uint d = 0; d < dims; d++)
                    elements *= reader.ReadUInt64();
                reader.ReadUInt32(); // 张量类型
                reader.ReadUInt64(); // 数据偏移
                parameterCount += elements;
            }

            return new GGufHeader
            {
                Version = version,
                Values = values,
                TensorCount = tensorCount,
                ParameterCount = parameterCount
            };
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("GGUF header is truncated.", e);
        }
    }

    private static ulong ReadCount(BinaryReader reader, ulong max)
    {
        ulong count = reader.ReadUInt64();
        if (count > max) throw new InvalidDataException($"Count {count} exceeds limit {max}.");
        return count;
    }

    private static string ReadString(BinaryReader reader)
    {
        ulong length = ReadCount(reader, MaxStringBytes);
        byte[] bytes = reader.ReadBytes((int)length);
        if ((ulong)bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void SkipString(BinaryReader reader)
    {
        Skip(reader, ReadCount(reader, MaxStringBytes));
    }

    private static object ReadScalar(BinaryReader reader, EValueType type)
    {
        return type switch
        {
            EValueType.UInt8 => reader.ReadByte(),
            EValueType.Int8 => reader.ReadSByte(),
            EValueType.UInt16 => reader.ReadUInt16(),
            EValueType.Int16 => reader.ReadInt16(),
            EValueType.UInt32 => reader.ReadUInt32(),
            EValueType.Int32 => reader.ReadInt32(),
            EValueType.Float32 => reader.ReadSingle(),
            EValueType.Bool => reader.ReadByte() != 0,
            EValueType.String => ReadString(reader),
            EValueType.UInt64 => reader.ReadUInt64(),
            EValueType.Int64 => reader.ReadInt64(),
            EValueType.Float64 => reader.ReadDouble(),
            _ => throw new InvalidDataException($"Unknown GGUF value type {(uint)type}.")
        };
    }

    // 词表等大数组只跳过；嵌套数组 llama.cpp 不支持，按非法处理
    private static void SkipArray(BinaryReader reader)
    {
        EValueType itemType = (EValueType)reader.ReadUInt32();
        ulong count = ReadCount(reader, MaxArrayCount);
        if (itemType == EValueType.String)
        {
            for (ulong i = 0; i < count; i++)
                SkipString(reader);
            return;
        }

        int itemSize = itemType switch
        {
            EValueType.UInt8 or EValueType.Int8 or EValueType.Bool => 1,
            EValueType.UInt16 or EValueType.Int16 => 2,
            EValueType.UInt32 or EValueType.Int32 or EValueType.Float32 => 4,
            EValueType.UInt64 or EValueType.Int64 or EValueType.Float64 => 8,
            _ => throw new InvalidDataException($"Unsupported GGUF array item type {(uint)itemType}.")
        };
        Skip(reader, count * (ulong)itemSize);
    }

    private static void Skip(BinaryReader reader, ulong bytes)
    {
        Stream stream = reader.BaseStream;
        if (stream.CanSeek)
        {
            if (stream.Position + (long)bytes > stream.Length) throw new EndOfStreamException();
            stream.Seek((long)bytes, SeekOrigin.Current);
            return;
        }

        Span<byte> buffer = stackalloc byte[4096];
        while (bytes > 0)
        {
            int read = stream.Read(buffer[..(int)Math.Min(bytes, (ulong)buffer.Length)]);
            if (read <= 0) throw new EndOfStreamException();
            bytes -= (ulong)read;
        }
    }
}
