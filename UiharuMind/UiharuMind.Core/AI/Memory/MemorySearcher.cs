using System.Text;
using CommunityToolkit.VectorData.SqliteVec;
using Microsoft.Extensions.VectorData;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 记忆检索。持有一个长命的只读集合句柄——检索发生在每轮对话里,不能每次都开库。
///
/// 与 <see cref="MemoryIndexBuilder"/> 分开的理由:构建是低频、写入、可取消的批处理,
/// 检索是高频、只读、必须快的路径,两者对"库不可用"的态度也相反——构建要把失败报给用户,
/// 检索失败只能安静退让,不能让一次记忆查不到就整轮对话失败。
/// </summary>
internal sealed class MemorySearcher : IDisposable
{
    /// <summary>
    /// 一次检索最多注入的 token 预算。
    ///
    /// 不按块数取:结构切块后块大小差异很大(一个小节可能几十字,也可能几百字),
    /// 固定块数会让注入量在两端都失控——块小则召回覆盖面太窄,块大则默默吃掉大量上下文。
    /// </summary>
    public const int DefaultTokenBudget = 1500;

    /// <summary>块数封顶。预算之外再加一道,避免碎块过多时塞进几十条互相重复的短片段</summary>
    public const int MaxChunkCount = 12;

    /// <summary>
    /// 采纳所需的最低相似度。低于它说明整个库都跟这次提问无关(典型如「你好」),
    /// 此时一块都不给——注入几百 token 无关设定,比什么都不注入更伤回答质量。
    ///
    /// 值只能是拍的:多语言嵌入模型普遍存在基线偏移,任意两段中文之间天然就有 0.4 上下的
    /// 相似度,而这个基线随模型而变。因此改嵌入模型后此处需要按 <see cref="Log.Debug"/>
    /// 打出的实际分数重调。
    /// </summary>
    public const float MinimumSimilarity = 0.35f;

    /// <summary>
    /// 相对首名允许落后的相似度。首名之下差这么多的块,与其说是次优答案不如说是陪跑,
    /// 收进来只会稀释上下文。
    ///
    /// 有了绝对下限还要这一道,是因为绝对下限只认「整体不相关」,认不出「首名很准、
    /// 其余勉强够线」——那种情况下把够线的全收,等于用噪音填满预算。
    /// </summary>
    public const float RelativeSimilarityDrop = 0.1f;

    /// <summary>向量库一次取回的候选数。要多于最终采纳数,才能在预算内挑到足够的块</summary>
    private const int CandidateCount = MaxChunkCount * 2;

    private readonly MemoryStore _store;
    private readonly Func<string> _nameProvider;
    private readonly Action<string> _errorReporter; //把"库开不了""维度不符"这类状态回传给门面落盘并刷 UI

    private SqliteCollection<string, MemoryChunkRecord>? _collection;
    private int? _embeddingDimensions;
    private bool _isVectorStoreUnavailable;
    private bool _disposed;

    /// <summary>
    /// 创建检索器
    /// </summary>
    /// <param name="store">库文件管理器</param>
    /// <param name="nameProvider">取当前记忆库名称的委托,仅用于日志</param>
    /// <param name="errorReporter">检索侧错误的回传出口</param>
    public MemorySearcher(MemoryStore store, Func<string> nameProvider, Action<string> errorReporter)
    {
        _store = store;
        _nameProvider = nameProvider;
        _errorReporter = errorReporter;
    }

    /// <summary>
    /// 检索相关片段
    /// </summary>
    /// <param name="session">可用的嵌入会话</param>
    /// <param name="query">查询词</param>
    /// <returns>拼好的片段文本;无结果或库不可用时为空串</returns>
    public async Task<string> SearchAsync(IEmbeddingSession session, string query)
    {
        if (_disposed || _isVectorStoreUnavailable || string.IsNullOrWhiteSpace(query)) return "";

        ReadOnlyMemory<float> queryEmbedding = await GenerateSearchEmbeddingAsync(session, query).ConfigureAwait(false);
        SqliteCollection<string, MemoryChunkRecord>? collection =
            await EnsureCollectionAsync(queryEmbedding.Length).ConfigureAwait(false);
        if (collection == null) return "";

        // 检索结果按距离升序返回,即相似度降序:首名就是最高分,后面只会更低。
        // 两道闸门与「第一块无论多大都收」都建立在这个前提上。
        List<ScoredChunk> accepted = [];
        int usedTokens = 0;
        float? topSimilarity = null;
        await foreach (VectorSearchResult<MemoryChunkRecord> result in collection.SearchAsync(
                           queryEmbedding, CandidateCount,
                           new VectorSearchOptions<MemoryChunkRecord> { IncludeVectors = false }))
        {
            float? similarity = ToSimilarity(result.Score);
            if (accepted.Count == 0)
            {
                // 首名都不够线,说明整个库与本次提问无关。
                // 分数缺失时放行:宁可多注入一次,也不能让一个拿不到的字段把知识库整个静默关掉
                if (similarity < MinimumSimilarity)
                {
                    Log.Debug($"Memory search rejected: {_nameProvider()}, top similarity {similarity:F3} " +
                              $"< {MinimumSimilarity:F2}");
                    return "";
                }

                topSimilarity = similarity;
            }
            else if (similarity < topSimilarity - RelativeSimilarityDrop)
            {
                break; //相似度降序,后面只会更差
            }

            int tokens = LlmTokenizer.CountTokens(result.Record.Text);

            // 第一块无论多大都收:预算比单块还小的时候,交一块总比交空手好
            if (accepted.Count > 0 && usedTokens + tokens > DefaultTokenBudget) break;

            accepted.Add(new ScoredChunk(result.Record, similarity));
            usedTokens += tokens;
            if (accepted.Count >= MaxChunkCount) break;
        }

        Log.Debug($"Memory search accepted: {_nameProvider()}, {accepted.Count} chunks, {usedTokens} tokens, " +
                  $"similarity {string.Join(", ", accepted.Select(x => x.Similarity?.ToString("F3") ?? "n/a"))}");
        return Format(accepted);
    }

    /// <summary>
    /// 余弦距离换算成相似度。库里配的是 <see cref="DistanceFunction.CosineDistance"/>,
    /// 连接器把它原样放进 <c>Score</c>——那是「越小越像」,与字面上的 Score 正好相反。
    /// 送进提示词的必须是「越大越像」,否则模型会优先采信最不相关的那块。
    /// </summary>
    /// <param name="distance">连接器给出的余弦距离;拿不到时为 null</param>
    /// <returns>相似度;距离缺失时返回 null,表示「无从判断」而非「不相关」</returns>
    private static float? ToSimilarity(double? distance) => distance == null ? null : (float)(1 - distance.Value);

    /// <summary>一条通过筛选的片段及其相似度;相似度为 null 表示连接器没给出距离</summary>
    private readonly record struct ScoredChunk(MemoryChunkRecord Record, float? Similarity);

    /// <summary>关掉集合句柄并清空维度缓存。换库前后都要调,否则会继续读已被移走的旧文件</summary>
    public void ResetCollection()
    {
        _collection?.Dispose();
        _collection = null;
        _embeddingDimensions = null;
        _isVectorStoreUnavailable = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _collection?.Dispose();
        _collection = null;
    }

    // 不输出 ChunkIndex:模型既不能凭编号取块,提示词里也禁止它提编号,写进去只是噪音
    private static string Format(List<ScoredChunk> memories)
    {
        if (memories.Count == 0) return "";

        StringBuilder sb = StringBuilderPool.Get();
        for (int i = 0; i < memories.Count; i++)
        {
            ScoredChunk chunk = memories[i];
            sb.AppendLine("SourceName: " + chunk.Record.SourceName);
            // 字段名是 Similarity 而非 Relevance:这个数只是余弦相似度,不等于「这段能回答问题」,
            // 叫 Relevance 等于向模型承诺了一个它给不了的判断。分数缺失时整行不写,免得模型瞎猜
            if (chunk.Similarity != null) sb.AppendLine("Similarity: " + chunk.Similarity.Value.ToString("F3"));
            sb.AppendLine("Content: " + chunk.Record.Text);
            if (i < memories.Count - 1) sb.AppendLine("\n***\n");
        }

        string text = sb.ToString();
        StringBuilderPool.Release(sb);
        return text;
    }

    private async Task<SqliteCollection<string, MemoryChunkRecord>?> EnsureCollectionAsync(int embeddingDimensions)
    {
        if (_isVectorStoreUnavailable) return null;
        if (_embeddingDimensions != null && _embeddingDimensions != embeddingDimensions)
        {
            _isVectorStoreUnavailable = true;
            _errorReporter("Memory vector dimension mismatch");
            return null;
        }

        if (_collection != null) return _collection;
        try
        {
            _embeddingDimensions = embeddingDimensions;
            _collection = await MemoryStore.CreateCollectionAsync(
                _store.DatabasePath, embeddingDimensions, CancellationToken.None).ConfigureAwait(false);
            return _collection;
        }
        catch (Exception e)
        {
            _isVectorStoreUnavailable = true;
            Log.Warning($"Memory vector store unavailable: {_nameProvider()}, {e.Message}");
            _errorReporter(e.Message);
            return null;
        }
    }

    private static async Task<ReadOnlyMemory<float>> GenerateSearchEmbeddingAsync(
        IEmbeddingSession session, string query)
    {
        string candidate = query.Trim();
        while (true)
        {
            try
            {
                return await session.GenerateEmbeddingAsync(candidate).ConfigureAwait(false);
            }
            catch (EmbeddingInputTooLargeException)
                when (MemoryTextChunker.CanSplitFurther(candidate))
            {
                // 检索只需要表达当前意图,过长时保留前半段,避免一次聊天查询拖垮记忆检索。
                candidate = candidate[..Math.Max(
                    MemoryTextChunker.MinimumSplitLength, candidate.Length / 2)].Trim();
            }
        }
    }
}
