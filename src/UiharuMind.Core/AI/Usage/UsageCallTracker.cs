using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// 一次「服务调用」的记账器：从发出到终态，<b>只写一行</b>。
///
/// <para>
/// 口径（设计文档 3.3）：写入不放在「收到 usage 即写」，而是放在一次服务调用的终态——
/// 成功收口写 ok=1；失败时若之前已收到 usage（<see cref="UiharuMind.Core.AI.Execution.StreamUsage"/> 会先补发
/// 最后一份再抛），把已收 token <b>并进失败行</b>（ok=0 带量），绝不另写第二行。
/// </para>
/// <para>
/// 计时对齐既有口径：发出时刻由调用方在 <c>thinkingStats.NoteCallStarted()</c> 处一起给，
/// 首帧在流的第一条内容上记。严格 TTFT 应在 HTTP 层，但这一层对「体验延迟」够用。
/// </para>
/// </summary>
public sealed class UsageCallTracker
{
    private readonly string _sessionId;
    private readonly UsageModelIdentity _model;
    private readonly Action<UsageRecord> _sink;
    private readonly long _startedTs;
    private readonly LlmRetryTally _retries = new();
    private bool _settled;
    private bool _receivedAny;
    private long? _firstTokenTs;
    private long _input;
    private long _cachedRead;
    private long? _cachedWrite;
    private long _output;
    private long _reasoning;

    private UsageCallTracker(string sessionId, UsageModelIdentity model, long startedTs,
        Action<UsageRecord>? sink)
    {
        _sessionId = sessionId;
        _model = model;
        _startedTs = startedTs;
        _sink = sink ?? UsageJournal.Instance.Append;
    }

    /// <summary>这一发内部的重发计数口；挂到 <see cref="LlmRequestScope"/> 上带出 attempt</summary>
    public LlmRetryTally Retries => _retries;

    /// <summary>已经写过终态（成功或失败）</summary>
    public bool IsSettled => _settled;

    /// <summary>
    /// 开始记一次服务调用
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="model">发请求的模型身份快照</param>
    /// <param name="startedTs">请求发出时刻；null 取当前</param>
    /// <param name="sink">写入目标；null 走进程内的 <see cref="UsageJournal"/></param>
    /// <returns>记账器</returns>
    public static UsageCallTracker Begin(string sessionId, UsageModelIdentity model, long? startedTs = null,
        Action<UsageRecord>? sink = null) =>
        new(sessionId, model, startedTs ?? DateTimeOffset.Now.ToUnixTimeMilliseconds(), sink);

    /// <summary>
    /// 流的首帧到了，记首 token 延迟起点（重复调用只认第一次）
    /// </summary>
    public void NoteFirstToken() => _firstTokenTs ??= DateTimeOffset.Now.ToUnixTimeMilliseconds();

    /// <summary>
    /// 收到一份用量。多次调用按增量累加（一次调用理论上只有一份，见 <c>StreamUsage.KeepLast</c>）
    /// </summary>
    /// <param name="details">服务端报的用量</param>
    public void NoteUsage(UsageDetails details)
    {
        if (_settled) return;
        _receivedAny = true;
        _input += details.InputTokenCount ?? 0;
        _output += details.OutputTokenCount ?? 0;
        _reasoning += details.ReasoningTokenCount ?? 0;
        _cachedRead += TurnUsageLedger.ReadCachedTokens(details);
        if (UsageTokens.ReadCachedWriteTokens(details) is { } cachedWrite)
            _cachedWrite = (_cachedWrite ?? 0) + cachedWrite;
    }

    /// <summary>
    /// 成功收口：写 ok=1 的一行
    /// </summary>
    public void Complete()
    {
        if (_settled) return;
        _settled = true;
        Write(true, null, null, null);
    }

    /// <summary>
    /// 旁路请求（写交接文档）的成功收口：一次调用可能含两次 SDK 往返（禁工具重发），
    /// 每条 usage 各一行；计时与重试只记在第一行，避免把总耗时重复分摊
    /// </summary>
    /// <param name="usages">这次旁路请求拿到的所有用量</param>
    public void CompleteWith(IReadOnlyList<UsageDetails> usages)
    {
        if (_settled) return;
        _settled = true;
        if (usages.Count == 0)
        {
            Write(true, null, null, null);
            return;
        }

        for (int i = 0; i < usages.Count; i++)
        {
            UsageDetails usage = usages[i];
            long input = usage.InputTokenCount ?? 0;
            long cachedRead = TurnUsageLedger.ReadCachedTokens(usage);
            long? cachedWrite = UsageTokens.ReadCachedWriteTokens(usage);
            long output = usage.OutputTokenCount ?? 0;
            long reasoning = usage.ReasoningTokenCount ?? 0;
            long? startedTs = i == 0 ? _startedTs : null;
            int? firstTokenMs = i == 0 && _firstTokenTs is { } first ? (int)(first - _startedTs) : null;
            int retries = i == 0 ? _retries.Retries : 0;
            Emit(input, cachedRead, cachedWrite, output, reasoning, startedTs,
                i == 0 ? ElapsedMs() : null, firstTokenMs, true, null, null, null, retries);
        }
    }

    /// <summary>
    /// 失败收口：写 ok=0 的一行，已收到的量并进来（<b>不另写第二行</b>）
    /// </summary>
    /// <param name="errorKind">错误类别（<see cref="UsageErrorKinds"/> 的常量）</param>
    /// <param name="error">异常；用于状态码与详情</param>
    /// <param name="statusCode">已知的 HTTP 状态码</param>
    /// <param name="detail">异常之外的补充说明（如「没产出正文」）</param>
    public void Fail(string errorKind, Exception? error = null, int? statusCode = null, string? detail = null)
    {
        if (_settled) return;
        _settled = true;
        Write(false, errorKind, detail ?? UsageErrorKinds.DetailOf(error),
            statusCode ?? UsageErrorKinds.StatusCodeOf(error));
    }

    /// <summary>
    /// 整轮级失败（异常跑出了单次调用之外）：记一行 0 token 的失败，
    /// 让错误率与失败分布看得见这件事。已经有在途调用时不要用这个——
    /// 那个调用自己的 <see cref="Fail"/> 会把量一起记进去
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="model">模型身份快照</param>
    /// <param name="errorKind">错误类别</param>
    /// <param name="error">异常</param>
    /// <param name="sink">写入目标；null 走进程内的 <see cref="UsageJournal"/></param>
    public static void RecordTurnFailure(string sessionId, UsageModelIdentity model, string errorKind,
        Exception? error, Action<UsageRecord>? sink = null)
    {
        UsageCallTracker tracker = Begin(sessionId, model, sink: sink);
        tracker.Fail(errorKind, error);
    }

    private long ElapsedMs() => DateTimeOffset.Now.ToUnixTimeMilliseconds() - _startedTs;

    /// <summary>失败行的延迟：一个字节都没收到时算「未测得」（null），而不是 0</summary>
    private long? FailureLatencyMs() => _receivedAny || _firstTokenTs != null ? ElapsedMs() : null;

    private void Write(bool ok, string? errorKind, string? errorDetail, int? statusCode) =>
        Emit(_input, _cachedRead, _cachedWrite, _output, _reasoning, _startedTs,
            ok ? ElapsedMs() : FailureLatencyMs(),
            _firstTokenTs is { } first ? (int)(first - _startedTs) : null, ok, errorKind, errorDetail, statusCode,
            _retries.Retries);

    private void Emit(long input, long cachedRead, long? cachedWrite, long output, long reasoning,
        long? startedTs, long? latencyMs, int? firstTokenMs, bool ok, string? errorKind, string? errorDetail,
        int? statusCode, int retryCount)
    {
        long ts = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        long started = startedTs ?? ts;
        UsagePriceSnapshot price = _model.PriceFor(input, cachedRead, output);
        UsageRecord record = new(
            ts,
            started,
            _sessionId,
            _model.ModelName,
            _model.ModelId,
            _model.ProviderId,
            _model.ProviderKey,
            _model.ApiBase,
            _model.IsLocal,
            input,
            cachedRead,
            cachedWrite,
            output,
            reasoning,
            latencyMs is { } latency ? (int)Math.Clamp(latency, 0, int.MaxValue) : null,
            firstTokenMs,
            ok,
            errorKind,
            errorDetail,
            statusCode,
            retryCount,
            price.CostUsd,
            price.Source,
            price.Input,
            price.Output,
            price.CacheRead,
            price.Multiplier);

        try
        {
            _sink(record);
        }
        catch (Exception e)
        {
            //记账永不影响请求：写不进去就只留一条日志（这条日志本身也不能抛）
            Log.Warning($"Usage record sink failed: {e.Message}");
        }
    }
}

/// <summary>
/// 从 <see cref="UsageDetails"/> 里尽力抠出各家自报的缓存字段
/// </summary>
public static class UsageTokens
{
    /// <summary>
    /// 缓存<b>写入</b> token。MEAI 的 <see cref="UsageDetails"/> 没有这个字段，OpenAI 兼容协议也不报，
    /// 只有 Anthropic 系的 <c>cache_creation_input_tokens</c> 会出现在附加计数里。
    /// 找不到就返回 null——NULL 的语义是「不知道」，不是 0（一排 0 会被读成「缓存写入是 0」）
    /// </summary>
    /// <param name="details">服务端报的用量</param>
    /// <returns>缓存写入 token；供应商没上报为 null</returns>
    public static long? ReadCachedWriteTokens(UsageDetails details)
    {
        if (details.AdditionalCounts is not { Count: > 0 } counts) return null;
        foreach (KeyValuePair<string, long> pair in counts)
        {
            bool isCache = pair.Key.Contains("cach", StringComparison.OrdinalIgnoreCase);
            bool isWrite = pair.Key.Contains("write", StringComparison.OrdinalIgnoreCase) ||
                           pair.Key.Contains("creation", StringComparison.OrdinalIgnoreCase);
            if (isCache && isWrite) return pair.Value;
        }

        return null;
    }
}
