using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 在一段 UTF-8 JSON 上记下若干「字节区间 → 替换字节」，最后一次拼出结果，没改到的区间原样拷贝。
/// 配合 <see cref="System.Text.Json.Utf8JsonReader"/> 用：边扫边记，扫完 <see cref="WriteTo"/>。
/// 不建 DOM、不经字符串，原文的转义与空白一字不动。
/// 改动按起点有序存放；常见只有一两处，前几处放在栈上，不分配。
/// </summary>
internal ref struct Utf8JsonSplice
{
    private const int InlineCapacity = 4;

    private InlineEdits _inline;
    private Edit[]? _spilled; //超出内联容量后整体挪到堆上
    private int _count;

    /// <summary>已记下的改动数</summary>
    public readonly int Count => _count;

    /// <summary>
    /// 把 [start, end) 换成 replacement；start 等于 end 即插入
    /// </summary>
    /// <param name="start">起点（含）</param>
    /// <param name="end">终点（不含）</param>
    /// <param name="replacement">替换字节，空数组即删除</param>
    public void Replace(int start, int end, byte[] replacement)
    {
        if (_spilled == null && _count == InlineCapacity)
        {
            _spilled = new Edit[InlineCapacity * 2];
            ((ReadOnlySpan<Edit>)_inline).CopyTo(_spilled);
        }
        else if (_spilled != null && _count == _spilled.Length)
        {
            Array.Resize(ref _spilled, _spilled.Length * 2);
        }

        Span<Edit> edits = _spilled != null ? _spilled : _inline;
        int index = _count;
        while (index > 0 && edits[index - 1].Start > start)
        {
            edits[index] = edits[index - 1];
            index--;
        }

        edits[index] = new Edit(start, end, replacement);
        _count++;
    }

    /// <summary>
    /// 删掉一个属性，连带一个逗号：后面有逗号删后面的，否则删前面的
    /// </summary>
    /// <param name="json">原文</param>
    /// <param name="keyStart">键的起点（引号处）</param>
    /// <param name="valueEnd">值的终点（不含）</param>
    public void RemoveProperty(ReadOnlySpan<byte> json, int keyStart, int valueEnd)
    {
        int after = valueEnd;
        while (after < json.Length && IsWhiteSpace(json[after])) after++;
        if (after < json.Length && json[after] == (byte)',')
        {
            Replace(keyStart, after + 1, []);
            return;
        }

        int before = keyStart - 1;
        while (before >= 0 && IsWhiteSpace(json[before])) before--;
        Replace(before >= 0 && json[before] == (byte)',' ? before : keyStart, valueEnd, []);
    }

    /// <summary>
    /// 按记下的改动拼出结果
    /// </summary>
    /// <param name="json">原文</param>
    /// <param name="output">写入拼好的 JSON</param>
    public readonly void WriteTo(ReadOnlySpan<byte> json, IBufferWriter<byte> output)
    {
        ReadOnlySpan<Edit> edits = _spilled != null
            ? _spilled.AsSpan(0, _count)
            : ((ReadOnlySpan<Edit>)_inline)[.._count];
        int size = json.Length;
        foreach (Edit edit in edits) size += edit.Replacement.Length - (edit.End - edit.Start);

        Span<byte> destination = output.GetSpan(size);
        int written = 0;
        int cursor = 0;
        foreach (Edit edit in edits)
        {
            Debug.Assert(edit.Start >= cursor, "改动区间重叠");
            json[cursor..edit.Start].CopyTo(destination[written..]);
            written += edit.Start - cursor;
            edit.Replacement.CopyTo(destination[written..]);
            written += edit.Replacement.Length;
            cursor = edit.End;
        }

        json[cursor..].CopyTo(destination[written..]);
        output.Advance(written + json.Length - cursor);
    }

    private static bool IsWhiteSpace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private readonly record struct Edit(int Start, int End, byte[] Replacement);

    [InlineArray(InlineCapacity)]
    private struct InlineEdits
    {
        private Edit _element;
    }
}
