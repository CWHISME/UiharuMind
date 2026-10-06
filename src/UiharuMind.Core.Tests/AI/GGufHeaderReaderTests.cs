using System.Text;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 纯托管 GGUF 头部解析与本地模型种类判定。
/// 种类决定模型进对话列表还是嵌入列表（ADR 0067），判错的代价是嵌入模型混进对话选择器。
/// </summary>
public class GGufHeaderReaderTests
{
    [Fact]
    public void ReadsScalars_SkipsArrays_AndSumsTensorElements()
    {
        byte[] bytes = new GGufBuilder()
            .String("general.architecture", "llama")
            .String("general.name", "Tiny")
            .UInt32("llama.context_length", 8192)
            .Float32("llama.rope.freq_base", 10000f)
            .Bool("tokenizer.ggml.add_bos_token", true)
            .StringArray("tokenizer.ggml.tokens", "a", "b", "c")
            .Int32Array("llama.attention.head_count_kv_per_layer", 8, 8)
            .Tensor("token_embd.weight", 64, 32)
            .Tensor("output_norm.weight", 64)
            .Build();

        GGufHeader header = GGufHeaderReader.Read(new MemoryStream(bytes));

        Assert.Equal(3u, header.Version);
        Assert.Equal("Tiny", header.GetString("general.name"));
        Assert.Equal(8192, header.GetInteger("llama.context_length"));
        Assert.False(header.Values.ContainsKey("tokenizer.ggml.tokens"));
        Assert.Equal(2ul, header.TensorCount);
        Assert.Equal(64ul * 32 + 64, header.ParameterCount);

        Dictionary<string, string> raw = header.ToStringDictionary();
        Assert.Equal("true", raw["tokenizer.ggml.add_bos_token"]);
        Assert.Equal("10000.000000", raw["llama.rope.freq_base"]);
    }

    [Fact]
    public void NonSeekableStream_ReadsTheSame()
    {
        byte[] bytes = new GGufBuilder()
            .StringArray("tokenizer.ggml.tokens", "x", "y")
            .String("general.architecture", "qwen3")
            .Build();

        GGufHeader header = GGufHeaderReader.Read(new ForwardOnlyStream(bytes));

        Assert.Equal("qwen3", header.GetString("general.architecture"));
    }

    [Fact]
    public void WrongMagic_IsRejected()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("NOTAGGUFFILE00000000000000");

        Assert.Throws<InvalidDataException>(() => GGufHeaderReader.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void TruncatedHeader_IsRejected()
    {
        byte[] bytes = new GGufBuilder().String("general.architecture", "llama").Build();

        Assert.Throws<InvalidDataException>(() =>
            GGufHeaderReader.Read(new MemoryStream(bytes[..^3])));
    }

    [Fact]
    public void AbsurdStringLength_IsRejectedBeforeAllocating()
    {
        MemoryStream stream = new();
        BinaryWriter writer = new(stream);
        writer.Write(0x46554747u);
        writer.Write(3u);
        writer.Write(0ul);
        writer.Write(1ul);
        writer.Write(ulong.MaxValue / 2); // 键长

        Assert.Throws<InvalidDataException>(() => GGufHeaderReader.Read(new MemoryStream(stream.ToArray())));
    }

    [Theory]
    [InlineData("llama", null, ELocalModelKind.Chat)]
    [InlineData("qwen3", 0L, ELocalModelKind.Chat)]
    [InlineData("bert", null, ELocalModelKind.Embedding)]
    [InlineData("nomic-bert", 1L, ELocalModelKind.Embedding)]
    [InlineData("qwen3", 3L, ELocalModelKind.Embedding)] //与对话同架构，只能靠 pooling 认
    [InlineData("xlm-roberta", 4L, ELocalModelKind.Reranker)] //编码器架构但池化是 RANK
    [InlineData("qwen3", 4L, ELocalModelKind.Reranker)]
    [InlineData("clip", null, ELocalModelKind.Projector)]
    public void Classify(string architecture, long? pooling, ELocalModelKind expected)
    {
        Assert.Equal(expected, LocalModelKindClassifier.Classify(architecture, pooling));
    }

    [Fact]
    public void Classify_FromHeader_UsesArchitecturePrefixedPooling()
    {
        byte[] bytes = new GGufBuilder()
            .String("general.architecture", "lfm2")
            .UInt32("lfm2.pooling_type", 1)
            .Build();

        GGufHeader header = GGufHeaderReader.Read(new MemoryStream(bytes));

        Assert.Equal(ELocalModelKind.Embedding, LocalModelKindClassifier.Classify(header));
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
