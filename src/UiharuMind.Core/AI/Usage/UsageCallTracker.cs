using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// 一轮对话的服务调用记账器：<b>每次服务调用写一行</b>，一轮可能写多行。
///
/// <para>
/// 口径与右侧用量面板（<c>TurnUsageLedger.Add</c>）同源：那里每收到一份用量
/// <c>Requests++</c>，这里就落一行——<b>一个 tracker 收到的每份 usage 各占一行</b>。
/// 不能把一轮的用量累加成一行：agent 档的一次 <c>run</c> 内部由框架自动跑工具往返
/// （<c>MaximumIterationsPerRequest</c> 高达 32768），一轮几十次调用是常态，
/// 累加会让「请求数」少记几十倍、单请求均值虚高、阶梯价按合计上下文换错档。
/// </para>
/// <para>
/// 写入不放在「收到 usage 即写」，而是攒着、除最后一份外立刻落盘：长轮跑到一半界面就能看到；
/// 最后一次收到的量要留着——中途断流时它属于失败的那一发，要并进失败行
/// （<see cref="StreamUsage"/> 会先补发最后一份再抛）。绝不把同一份量写两遍。
/// </para>
/// <para>
/// 计时：整轮的发出时刻由调用方给（与 <c>thinkingStats.NoteCallStarted()</c> 同一处）；
/// 每次调用自己的起点由 <see cref="NoteCallBoundary"/> 推后（工具结果落进流里），
/// 首帧在流的首条内容上记。严格 TTFT 应在 HTTP 层，这一层对「体验延迟」够用。
/// </para>
/// </summary>
public sealed class UsageCallTracker
{
    private readonly string _sessionId;
    private readonly UsageModelIdentity _model;
    private readonly Action<UsageRecord> _sink;
    private readonly long _startedTs;
    private readonly LlmRetryTally _retries = new();

    /// <summary>已收到用量、还没写盘的调用，按到达顺序</summary>
    private readonly List<CapturedCall> _calls = [];

    private bool _settled;
    private long _callStartedTs; //当前这次调用的发出时刻
    private long? _firstTokenTs; //当前这次调用的首帧
    private int _retriesSeen; //已经分摊到某一行上的重发次数

    /// <summary>一次调用的定格：用量与它自己的计时，收口时才变成一行</summary>
    private readonly record struct CapturedCall(long Ts, long StartedTs, int? FirstTokenMs, int? LatencyMs,
        int RetryCount, long Input, long CachedRead, long? CachedWrite, long Output, long Reasoning);

    private UsageCallTracker(string sessionId, UsageModelIdentity model, long startedTs,
        Action<UsageRecord>? sink)
    {
        _sessionId = sessionId;
        _model = model;
        _startedTs = startedTs;
        _callStartedTs = startedTs;
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
        new(sessionId, model, startedTs ?? Now(), sink);

    /// <summary>
    /// 流的首帧到了，记首 token 延迟起点（同一次调用内重复调用只认第一次）
    /// </summary>
    public void NoteFirstToken() => _firstTokenTs ??= Now();

    /// <summary>
    /// 一次服务调用的边界：工具结果落进流里（它是下一次调用的输入）。
    /// 下一次调用的计时从这里重新起算（工具执行的耗时不算进下一次调用的延迟），首帧重新等。
    ///
    /// <b>不用消息边界</b>（<c>MessageBoundaryContent</c>）：那是落盘时补发的，实测可能晚于下一次调用的首条内容十几秒，
    /// 拿它重置会把延迟量成接近 0
    /// </summary>
    public void NoteCallBoundary()
    {
        _callStartedTs = Now();
        _firstTokenTs = null;
    }

    /// <summary>
    /// 收到一份用量 = 一次服务调用收口。<b>按调用各记一行</b>，不累加。
    /// 除了最后一份，前面的立刻落盘：长轮跑到一半时界面就能看到（最后一份留着，它可能要并进失败行）
    /// </summary>
    /// <param name="details">服务端报的这一次用量</param>
    public void NoteUsage(UsageDetails details)
    {
        if (_settled) return;
        _calls.Add(Capture(details, _callStartedTs));
        //下一次调用紧接在这一发之后；工具执行的耗时由 NoteCallBoundary 再推后
        _callStartedTs = Now();
        _firstTokenTs = null;
        FlushSettledCalls();
    }

    /// <summary>
    /// 成功收口：每次服务调用各写一行
    /// </summary>
    public void Complete()
    {
        if (_settled) return;
        _settled = true;
        if (_calls.Count == 0)
        {
            //一次用量都没报（供应商不报 usage）：整轮记一行 0 token，别把这次请求整个丢掉
            EmitCall(ZeroCall(true), true);
            return;
        }

        foreach (CapturedCall call in _calls) EmitCall(call, true);
    }

    /// <summary>
    /// 旁路请求（写交接文档）的成功收口：一次调用可能含两次 SDK 往返（禁工具重发），
    /// 每条 usage 各一行；计时只记在第一行（总耗时没法拆），重发总数记在最后一行
    /// </summary>
    /// <param name="usages">这次旁路请求拿到的所有用量</param>
    public void CompleteWith(IReadOnlyList<UsageDetails> usages)
    {
        if (_settled) return;
        _settled = true;
        if (usages.Count == 0)
        {
            EmitCall(ZeroCall(true), true);
            return;
        }

        long now = Now();
        for (int i = 0; i < usages.Count; i++)
        {
            bool isFirst = i == 0;
            bool isLast = i == usages.Count - 1;
            EmitCall(new CapturedCall(
                now,
                isFirst ? _startedTs : now,
                isFirst ? FirstTokenMs() : null,
                isFirst ? (int)Math.Clamp(ElapsedMs(), 0, int.MaxValue) : null,
                isLast ? _retries.Retries : 0,
                usages[i].InputTokenCount ?? 0,
                UsageTokens.ReadCachedTokens(usages[i]),
                UsageTokens.ReadCachedWriteTokens(usages[i]),
                usages[i].OutputTokenCount ?? 0,
                usages[i].ReasoningTokenCount ?? 0), true);
        }
    }

    /// <summary>
    /// 失败收口。在此之前已经完整收到用量的各次调用各写一行（它们成功了）；
    /// 最后一次收到的量属于失败的那一发，<b>并进失败行</b>，不另写第二行
    /// </summary>
    /// <param name="errorKind">错误类别（<see cref="UsageErrorKinds"/> 的常量）</param>
    /// <param name="error">异常；用于状态码与详情</param>
    /// <param name="statusCode">已知的 HTTP 状态码</param>
    /// <param name="detail">异常之外的补充说明（如「没产出正文」）</param>
    public void Fail(string errorKind, Exception? error = null, int? statusCode = null, string? detail = null)
    {
        if (_settled) return;
        _settled = true;

        for (int i = 0; i < _calls.Count - 1; i++) EmitCall(_calls[i], true);

        string? errorDetail = detail ?? UsageErrorKinds.DetailOf(error);
        int? errorStatus = statusCode ?? UsageErrorKinds.StatusCodeOf(error);
        if (_calls.Count == 0)
        {
            EmitCall(ZeroCall(false), false, errorKind, errorDetail, errorStatus);
            return;
        }

        // 在途那一发：延迟算到失败为止，重发数是它自己那一段的增量
        CapturedCall inFlight = _calls[^1] with
        {
            LatencyMs = (int)Math.Clamp(Now() - _calls[^1].StartedTs, 0, int.MaxValue),
            RetryCount = _retries.Retries - _retriesSeen,
        };
        EmitCall(inFlight, false, errorKind, errorDetail, errorStatus);
    }

    /// <summary>把已确定收口的调用（除最后一份）立刻落盘，最后一份留在手里等终态</summary>
    private void FlushSettledCalls()
    {
        int count = _calls.Count - 1;
        if (count <= 0) return;
        for (int i = 0; i < count; i++) EmitCall(_calls[i], true);
        _calls.RemoveRange(0, count);
    }

    /// <summary>把「刚收到的一份用量」定格成一次调用（重发数按本发增量算）</summary>
    private CapturedCall Capture(UsageDetails details, long startedTs)
    {
        long now = Now();
        int retries = _retries.Retries;
        CapturedCall call = new(
            now,
            startedTs,
            _firstTokenTs is { } first ? (int)Math.Max(0, first - startedTs) : null,
            (int)Math.Clamp(now - startedTs, 0, int.MaxValue),
            retries - _retriesSeen,
            details.InputTokenCount ?? 0,
            UsageTokens.ReadCachedTokens(details),
            UsageTokens.ReadCachedWriteTokens(details),
            details.OutputTokenCount ?? 0,
            details.ReasoningTokenCount ?? 0);
        _retriesSeen = retries;
        return call;
    }

    private static long Now() => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    private long ElapsedMs() => Now() - _startedTs;

    private int? FirstTokenMs() => _firstTokenTs is { } first ? (int)Math.Max(0, first - _startedTs) : null;

    /// <summary>整轮一行、0 token：只用在「没有报任何用量」那条路上</summary>
    private CapturedCall ZeroCall(bool ok) => new(
        Now(), _startedTs, FirstTokenMs(),
        ok ? (int)Math.Clamp(ElapsedMs(), 0, int.MaxValue) : FailureLatencyMs(),
        _retries.Retries, 0, 0, null, 0, 0);

    /// <summary>失败行的延迟：一个字节都没收到时算「未测得」（null），而不是 0</summary>
    private int? FailureLatencyMs() => _firstTokenTs != null
        ? (int)Math.Clamp(ElapsedMs(), 0, int.MaxValue)
        : null;

    private void EmitCall(CapturedCall call, bool ok, string? errorKind = null, string? errorDetail = null,
        int? statusCode = null)
    {
        long started = call.StartedTs;
        try
        {
            //计价放在 try 里：价格表解析出问题也只丢这一条的账，不能把整轮对话带走
            UsagePriceSnapshot price = _model.PriceFor(call.Input, call.CachedRead, call.Output);
            UsageRecord record = new(
                call.Ts,
                started,
                _sessionId,
                _model.ModelName,
                _model.ModelId,
                _model.ProviderId,
                _model.ProviderKey,
                _model.ApiBase,
                _model.IsLocal,
                call.Input,
                call.CachedRead,
                call.CachedWrite,
                call.Output,
                call.Reasoning,
                call.LatencyMs,
                call.FirstTokenMs,
                ok,
                errorKind,
                errorDetail,
                statusCode,
                call.RetryCount,
                price.CostUsd,
                price.Source,
                price.Input,
                price.Output,
                price.CacheRead,
                price.Multiplier);

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
    /// 命中前缀缓存的输入 token。各家键名不同（OpenAI 系是 <c>cached_tokens</c>，经 MEAI 映射后还会再改一次名），
    /// 所以按子串命中而不是写死键名；找不到就是 0
    /// </summary>
    /// <param name="details">服务端报的用量</param>
    /// <returns>命中的输入 token 数</returns>
    public static long ReadCachedTokens(UsageDetails details)
    {
        if (details.CachedInputTokenCount != null) return (long)details.CachedInputTokenCount;
        if (details.AdditionalCounts == null) return 0;

        // 键名各家不同。注意 DeepSeek 会同时报 prompt_cache_hit_tokens 与
        // prompt_cache_miss_tokens 两个键——两者都含 "cach"，字典遍历顺序不定，
        // 先到的未必是命中数。所以优先匹配明确含 "hit" 的键，找不到再退回纯 "cach" 键。
        long? cached = null;
        foreach (KeyValuePair<string, long> pair in details.AdditionalCounts)
        {
            if (pair.Key.Contains("hit", StringComparison.OrdinalIgnoreCase) &&
                pair.Key.Contains("cach", StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }

            if (pair.Key.Contains("cach", StringComparison.OrdinalIgnoreCase) && cached == null)
            {
                cached = pair.Value;
            }
        }

        return cached ?? 0;
    }

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
