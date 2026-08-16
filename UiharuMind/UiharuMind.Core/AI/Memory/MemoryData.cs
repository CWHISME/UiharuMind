using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 一个知识库：元数据是它的本体（会被序列化成 json），索引构建与检索委派给协作者。
///
/// 这里刻意只留门面：<see cref="UpdateIndexAsync"/> 与 <see cref="GetLongTermMemory"/> 的签名
/// 是四个调用点（知识库工具、上下文注入、编辑窗、选择窗）依赖的边界，实现搬家不该惊动它们。
/// 真正干活的三个协作者见 <see cref="MemoryStore"/>、<see cref="MemorySearcher"/>、
/// <see cref="MemoryIndexBuilder"/>。
///
/// 协作者是懒创建的：容器在启动时会把所有知识库一次性反序列化出来，构造时就开 SQLite 连接
/// 等于让启动时间跟知识库数量成正比，而多数知识库这次启动根本不会被用到。
/// </summary>
public class MemoryData : IUniquieContainerItem, IDisposable
{
    /// <summary>
    /// 当前切块规则的版本。切块边界、上下文拼接方式、记录列一改，旧索引就是按旧规则建的——
    /// 仍然能打开、仍然能检索，只是质量还是老样子，而用户不会知道该重建。
    /// 抬这个数会让加载时把索引标脏，走现成的「需要更新索引」提示。
    /// </summary>
    public const int CurrentIndexVersion = 4;

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<MemoryTextSource> TextSources { get; set; } = [];
    public List<string> FilePaths { get; set; } = [];
    public bool IndexDirty { get; set; }
    public DateTime? LastIndexedAt { get; set; }
    public string LastIndexError { get; set; } = "";

    /// <summary>建立当前索引时用的切块规则版本。0 表示这个字段出现之前建的索引</summary>
    public int IndexVersion { get; set; }

    public event Action? StateChanged;

    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private MemoryStore? _store;
    private MemorySearcher? _searcher;
    private MemoryIndexBuilder? _builder;
    private IEmbeddingSession? _embeddingSession; //归 EmbeddingModelService 所有,这里只缓存引用,绝不 Dispose
    private bool _disposed;

    /// <summary>索引是否按当前切块规则建的。false 表示能用但质量是旧的</summary>
    public bool IsIndexVersionCurrent => IndexVersion == CurrentIndexVersion;

    /// <summary>
    /// 检索与查询相关的记忆片段
    /// </summary>
    /// <param name="query">查询词</param>
    /// <param name="asChunks">保留参数,当前实现始终按块返回</param>
    /// <returns>拼好的片段文本;不可用或无结果时为空串</returns>
    public async Task<string> GetLongTermMemory(string query, bool asChunks = true)
    {
        try
        {
            if (_disposed) return "";
            if (!await EnsureReadyForSearchAsync().ConfigureAwait(false)) return "";
            if (_embeddingSession == null) return "";

            return await Searcher.SearchAsync(_embeddingSession, query).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            return "";
        }
    }

    /// <summary>
    /// 重建索引
    /// </summary>
    /// <param name="progress">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新结果</returns>
    public async Task<MemoryIndexUpdateResult> UpdateIndexAsync(
        IProgress<MemoryIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool lockAcquired = false;
        try
        {
            await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockAcquired = true;

            // 先报一次「准备中」再去要嵌入会话:模型没加载时 GetSessionAsync 要花上几十秒起模型,
            // 这期间一个进度事件都不发的话,界面就是「正在更新 + 0% + 无阶段文字」,看着和卡死一样。
            progress?.Report(new MemoryIndexProgress(
                MemoryIndexStage.Preparing, 0.02, "", 0, SourceCount, 0, 0, 0));

            if (!await EnsureReadyForSearchAsync(cancellationToken).ConfigureAwait(false) ||
                _embeddingSession == null)
            {
                return FailUpdate(LastIndexError, []);
            }

            MemoryIndexBuildOutcome outcome = await Builder.BuildAsync(
                _embeddingSession, BuildSourceReferences(),
                () => Searcher.ResetCollection(), progress, cancellationToken).ConfigureAwait(false);

            return ApplyOutcome(outcome);
        }
        catch (OperationCanceledException)
        {
            IndexDirty = true;
            SaveIndexState();
            return new MemoryIndexUpdateResult(MemoryIndexUpdateStatus.Cancelled, []);
        }
        finally
        {
            if (lockAcquired) _indexLock.Release();
        }
    }

    /// <summary>
    /// 校验一个文本文件能否作为来源加入
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取结果,失败时带结构化错误码</returns>
    public async Task<MemorySourceReadResult> ValidateTextFileAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var source = new MemorySourceReference(
            MemorySourceId.FromValue(filePath), Path.GetFileName(filePath),
            MemorySourceKind.PlainTextFile, filePath);
        return await MemorySourceReaders.ReadAsync(source, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 确认嵌入模型可用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可用返回 True,失败原因写入 <see cref="LastIndexError"/></returns>
    public async Task<bool> EnsureReadyForSearchAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(Name))
        {
            LastIndexError = "Memory name not set";
            Log.Error(LastIndexError);
            return false;
        }

        return await EnsureEmbeddingSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 标记索引已过期（来源增删改后调用），并把该状态与清空的错误一起落盘
    /// </summary>
    public void MarkIndexDirty()
    {
        IndexDirty = true;
        LastIndexError = "";
        SaveIndexState();
    }

    /// <summary>
    /// 仅把当前元数据落盘，不改动索引状态（改名称、描述等）
    /// </summary>
    public void SaveMetadata()
    {
        SaveIndexState();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // _embeddingSession 归 EmbeddingModelService 管:它是全进程共享的一个 session,
        // 在这里 Dispose 会把别的知识库和别处的嵌入调用一起搞死。
        _searcher?.Dispose();
        _searcher = null;
        _indexLock.Dispose();
    }

    /// <summary>库文件管理器。删除或改名时容器要靠它动索引文件</summary>
    internal MemoryStore Store => _store ??= new MemoryStore(() => Name);

    /// <summary>
    /// 关掉检索侧的集合句柄。改名前必须调——句柄占着,库文件在 Windows 上搬不动;
    /// 而且改名后路径已变,旧句柄指向的是搬走前的文件。
    /// </summary>
    internal void ResetSearchState()
    {
        _searcher?.ResetCollection();
    }

    private MemorySearcher Searcher => _searcher ??= new MemorySearcher(Store, () => Name, ReportSearchError);

    private MemoryIndexBuilder Builder => _builder ??= new MemoryIndexBuilder(Store);

    private int SourceCount => TextSources.Count + FilePaths.Count;

    /// <summary>检索侧发现库开不了或维度不符时的回传出口:落盘并刷 UI,否则用户只看到「搜不到」</summary>
    private void ReportSearchError(string error)
    {
        LastIndexError = error;
        StateChanged?.Invoke();
    }

    private List<MemorySourceReference> BuildSourceReferences()
    {
        List<MemorySourceReference> sources = [];
        sources.AddRange(TextSources.Select(source => new MemorySourceReference(
            source.Id, source.Title, MemorySourceKind.ManualText, Content: source.Content)));
        sources.AddRange(FilePaths.Select(path => new MemorySourceReference(
            MemorySourceId.FromValue(path), Path.GetFileName(path), MemorySourceKind.PlainTextFile, path)));
        return sources;
    }

    private MemoryIndexUpdateResult ApplyOutcome(MemoryIndexBuildOutcome outcome)
    {
        switch (outcome.Status)
        {
            case MemoryIndexUpdateStatus.Succeeded:
                IndexDirty = false;
                LastIndexError = "";
                LastIndexedAt = DateTime.UtcNow;
                IndexVersion = CurrentIndexVersion;
                SaveIndexState();
                return new MemoryIndexUpdateResult(MemoryIndexUpdateStatus.Succeeded, outcome.Failures);

            case MemoryIndexUpdateStatus.Cancelled:
                IndexDirty = true;
                SaveIndexState();
                return new MemoryIndexUpdateResult(MemoryIndexUpdateStatus.Cancelled, outcome.Failures);

            default:
                return FailUpdate(outcome.Error, outcome.Failures);
        }
    }

    private MemoryIndexUpdateResult FailUpdate(
        string error, IReadOnlyList<MemoryIndexSourceFailure> failures)
    {
        IndexDirty = true;
        LastIndexError = string.IsNullOrWhiteSpace(error) ? "Memory index update failed" : error;
        SaveIndexState();
        return new MemoryIndexUpdateResult(MemoryIndexUpdateStatus.Failed, failures, LastIndexError);
    }

    private void SaveIndexState()
    {
        MemoryManager.Instance.Save(this);
        StateChanged?.Invoke();
    }

    private async Task<bool> EnsureEmbeddingSessionAsync(CancellationToken cancellationToken)
    {
        if (_embeddingSession is { IsRunning: true }) return true;
        try
        {
            _embeddingSession = await EmbeddingModelService.Instance
                .GetSessionAsync(cancellationToken)
                .ConfigureAwait(false);
            LastIndexError = "";
            return true;
        }
        catch (Exception e)
        {
            LastIndexError = e is EmbeddingRuntimeException ? e.Message : "Embedding model startup failed.";
            Log.Error($"Embedding session unavailable: {e.Message}");
            return false;
        }
    }
}
