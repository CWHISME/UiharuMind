using System.Text;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 手搓最小 GGUF（v3）字节，只含测试关心的键值与张量信息
/// </summary>
internal sealed class GGufBuilder
{
    private readonly List<Action<BinaryWriter>> _kvs = [];
    private readonly List<Action<BinaryWriter>> _tensors = [];

    public GGufBuilder String(string key, string value) => Kv(key, 8, w => WriteString(w, value));
    public GGufBuilder UInt32(string key, uint value) => Kv(key, 4, w => w.Write(value));
    public GGufBuilder Float32(string key, float value) => Kv(key, 6, w => w.Write(value));
    public GGufBuilder Bool(string key, bool value) => Kv(key, 7, w => w.Write((byte)(value ? 1 : 0)));

    public GGufBuilder StringArray(string key, params string[] items) => Kv(key, 9, w =>
    {
        w.Write(8u);
        w.Write((ulong)items.Length);
        foreach (string item in items) WriteString(w, item);
    });

    public GGufBuilder Int32Array(string key, params int[] items) => Kv(key, 9, w =>
    {
        w.Write(5u);
        w.Write((ulong)items.Length);
        foreach (int item in items) w.Write(item);
    });

    public GGufBuilder Tensor(string name, params ulong[] dims)
    {
        _tensors.Add(w =>
        {
            WriteString(w, name);
            w.Write((uint)dims.Length);
            foreach (ulong dim in dims) w.Write(dim);
            w.Write(0u);
            w.Write(0ul);
        });
        return this;
    }

    public void WriteTo(string path) => File.WriteAllBytes(path, Build());

    public byte[] Build()
    {
        MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(0x46554747u);
        writer.Write(3u);
        writer.Write((ulong)_tensors.Count);
        writer.Write((ulong)_kvs.Count);
        foreach (Action<BinaryWriter> kv in _kvs) kv(writer);
        foreach (Action<BinaryWriter> tensor in _tensors) tensor(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private GGufBuilder Kv(string key, uint type, Action<BinaryWriter> writeValue)
    {
        _kvs.Add(w =>
        {
            WriteString(w, key);
            w.Write(type);
            writeValue(w);
        });
        return this;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }
}
