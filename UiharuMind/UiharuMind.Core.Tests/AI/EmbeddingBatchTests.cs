using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 批量嵌入的契约。
///
/// 批量是纯性能改动，但它有一个静默的坏法：向量与文本配错。配错不会报错，
/// 只会让每一块都带上别块的向量——检索结果从此毫无道理，而且看不出是哪一步坏的。
/// 所以「顺序与数量必须与入参一致」这条要单独钉住。
/// </summary>
public class EmbeddingBatchTests
{
    /// <summary>
    /// 响应按 index 归位，而不是按出现顺序。
    /// 规范没保证 data 数组有序，按出现顺序收就会把向量配错块。
    /// </summary>
    [Fact]
    public void ParseEmbeddings_HonorsIndexRatherThanArrayOrder()
    {
        const string json = """
                            {"data":[
                              {"index":1,"embedding":[0.0,1.0]},
                              {"index":0,"embedding":[1.0,0.0]}
                            ]}
                            """;

        List<ReadOnlyMemory<float>> vectors = OpenAICompatibleEmbeddingSession.ParseEmbeddings(json);

        Assert.Equal(2, vectors.Count);
        Assert.Equal(1.0f, vectors[0].Span[0], 3);
        Assert.Equal(0.0f, vectors[0].Span[1], 3);
        Assert.Equal(0.0f, vectors[1].Span[0], 3);
        Assert.Equal(1.0f, vectors[1].Span[1], 3);
    }

    /// <summary>没有 index 字段的端点按出现顺序收，不能整批丢掉</summary>
    [Fact]
    public void ParseEmbeddings_FallsBackToArrayOrderWhenIndexMissing()
    {
        const string json = """{"data":[{"embedding":[1.0,0.0]},{"embedding":[0.0,1.0]}]}""";

        List<ReadOnlyMemory<float>> vectors = OpenAICompatibleEmbeddingSession.ParseEmbeddings(json);

        Assert.Equal(2, vectors.Count);
        Assert.Equal(1.0f, vectors[0].Span[0], 3);
        Assert.Equal(1.0f, vectors[1].Span[1], 3);
    }

    /// <summary>向量要归一化,否则余弦距离的比较基准跟着模长跑</summary>
    [Fact]
    public void ParseEmbeddings_NormalizesVectors()
    {
        const string json = """{"data":[{"index":0,"embedding":[3.0,4.0]}]}""";

        List<ReadOnlyMemory<float>> vectors = OpenAICompatibleEmbeddingSession.ParseEmbeddings(json);

        Assert.Equal(0.6f, vectors[0].Span[0], 3);
        Assert.Equal(0.8f, vectors[0].Span[1], 3);
    }

    /// <summary>响应没有 data 数组时明确报错,而不是安静地返回空</summary>
    [Theory]
    [InlineData("""{"error":"boom"}""")]
    [InlineData("""{"data":"not an array"}""")]
    public void ParseEmbeddings_ThrowsOnMalformedResponse(string json)
    {
        Assert.Throws<EmbeddingRuntimeException>(() => OpenAICompatibleEmbeddingSession.ParseEmbeddings(json));
    }

    /// <summary>
    /// 默认实现逐条调用,数量与顺序必须与入参一致。
    /// 本地后端不覆写批量方法,走的就是这条,配错了整个本地索引都是错的。
    /// </summary>
    [Fact]
    public async Task DefaultBatchImplementation_PreservesOrderAndCount()
    {
        var fake = new SequentialFakeSession();
        IEmbeddingSession session = fake; //默认接口方法只能经接口调用

        IReadOnlyList<ReadOnlyMemory<float>> vectors =
            await session.GenerateEmbeddingsAsync(["a", "b", "c"]);

        Assert.Equal(3, vectors.Count);
        Assert.Equal(1f, vectors[0].Span[0]);
        Assert.Equal(2f, vectors[1].Span[0]);
        Assert.Equal(3f, vectors[2].Span[0]);
        Assert.Equal(["a", "b", "c"], fake.SeenTexts);
    }

    /// <summary>空入参不该发请求,也不该抛</summary>
    [Fact]
    public async Task DefaultBatchImplementation_HandlesEmptyInput()
    {
        var fake = new SequentialFakeSession();
        IEmbeddingSession session = fake;

        Assert.Empty(await session.GenerateEmbeddingsAsync([]));
        Assert.Empty(fake.SeenTexts);
    }

    /// <summary>
    /// 超长块的对半拆分必须收敛：拆出来的两段都要短于原文，
    /// 否则「被拒 → 拆 → 再被拒」会变成死循环，表现是索引永远卡在同一个百分比。
    /// </summary>
    [Fact]
    public void SplitOversized_AlwaysShrinks()
    {
        string[] samples =
        [
            new string('a', 200),
            string.Concat(Enumerable.Repeat("记忆索引", 50)),
            "aaaa bbbb cccc dddd",
            "前半段内容。后半段内容",
            new string('a', 100) + " " + new string('b', 100)
        ];

        foreach (string sample in samples)
        {
            (string first, string second) = MemoryTextChunker.SplitOversized(sample);

            Assert.True(first.Length < sample.Length, $"前段没变短：{sample.Length} -> {first.Length}");
            Assert.True(second.Length < sample.Length, $"后段没变短：{sample.Length} -> {second.Length}");
            Assert.NotEqual(0, first.Length);
            Assert.NotEqual(0, second.Length);
        }
    }

    /// <summary>逐条返回递增向量的假会话，用来验证默认批量实现的顺序</summary>
    private sealed class SequentialFakeSession : IEmbeddingSession
    {
        private int _counter;

        public List<string> SeenTexts { get; } = [];

        public string BackendName => "Fake";
        public string ModelPath => "";
        public int Dimensions => 1;
        public bool IsRunning => true;
        public string LastError => "";

        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
            string text, CancellationToken cancellationToken = default)
        {
            SeenTexts.Add(text);
            return Task.FromResult<ReadOnlyMemory<float>>(new[] { (float)++_counter });
        }

        public void Dispose()
        {
        }
    }
}
