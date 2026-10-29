/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// token 账本：本轮用量、会话累计用量与输入框估算，以及它们的显示文本格式化。
/// 与会话本体的往返只有一对：<see cref="Record"/> 记一次并写回，<see cref="RestoreFrom"/> 读回来——
/// 哪些数随本体落盘只在这两处定，加一个字段不会只存不读（缓存命中与本轮用量就曾这样重开即丢）。
/// </summary>
public sealed class TurnUsageLedger
{
    /// <summary>本轮输入 token（一轮多次工具往返时为各次之和，是成本视角）</summary>
    public long TurnInput { get; private set; }

    /// <summary>
    /// 本轮命中前缀缓存的输入 token。<b>与 <see cref="TurnInput"/> 同源累加</b>——
    /// 分母在哪涨分子就得在哪涨，两边不是同一批请求的话算出来的命中率不是比例
    /// </summary>
    public long TurnCachedInput { get; private set; }

    /// <summary>
    /// <b>报告占用</b>：最近一次响应里服务端报的输入 token。不能拿 <see cref="TurnInput"/> 顶替：
    /// 那是本轮累加值，一轮十几次工具往返能累到四十几万，与占用无关。
    ///
    /// ⚠️ 这个数<b>不一定是全量</b>。实测 GLM4-Flash 的 <c>prompt_tokens</c> 不含工具定义，
    /// 少报近一半。要判断「还剩多少空间」一律用 <see cref="EffectiveInput"/>；
    /// 这一个只用于记账与进度条上「服务端说的那一段」。见 ADR 0009。
    /// </summary>
    public long LastInput { get; private set; }

    /// <summary>
    /// 我们自己估的本轮输入（固定开销 + 历史），由执行侧每次记账时写入。
    /// 0 表示还没估过（没装配过，或不走 harness 的形态）。
    /// </summary>
    public long EstimatedInput { get; set; }

    /// <summary>
    /// 本轮固定开销的估算（系统提示 + 工具定义），是 <see cref="EstimatedInput"/> 里不随历史增长的那半。
    /// 单独留一份是因为折叠与截断的水位要按它换算——那两条乘在<b>历史额度</b>上，
    /// 而进度条那根轴是全量，两者相差正是这个数。
    /// </summary>
    public int FixedOverhead { get; set; }

    /// <summary>
    /// <b>有效占用</b>：报告占用与我们的估算取大，是「还剩多少空间」的唯一口径。
    ///
    /// 取大的方向是有意的，因为两侧代价不对称：晚压一步是请求撞上下文上限直接失败、
    /// 通常发生在 agent 跑了十几轮工具之后，那一轮全废；早压一步只是多写一次交接文档。
    /// </summary>
    public long EffectiveInput => Math.Max(LastInput, EstimatedInput);

    /// <summary>
    /// 服务端未计入的那部分（有效占用减去报告占用）。进度条画成第二段——
    /// 服务端的数原样显示可对账，差额标为「未计入」而不是被抹掉或被顶替。
    /// 两者口径一致时恒为 0，那一段也就不出现。
    /// </summary>
    public long UnreportedInput => Math.Max(0, EstimatedInput - LastInput);

    /// <summary>
    /// 最近一次请求发出前被折叠或截断压掉的历史估算，由执行侧每次记账时写入。没压时为 0
    /// </summary>
    public long CompactedInput { get; set; }

    /// <summary>
    /// <b>原始占用</b>：有效占用加上被压掉的那截，即供给窗口里实际攒下了多少。交接文档水位读它。
    ///
    /// 不能只读有效占用：折叠把发出去的那份压回交接线之下，交接就被它一直压住——
    /// 而交接替换的正是原始历史，越晚写，原始历史越大。没压时与有效占用相等
    /// </summary>
    public long RawInput => EffectiveInput + CompactedInput;

    /// <summary>当前模型的上下文窗口，0 表示未知（占用段整段省略）</summary>
    public int ContextLength { get; set; }

    /// <summary>
    /// 最近一次响应里命中前缀缓存的输入 token，0 表示未命中或服务端不报。
    /// 服务端不报这个数的话就无从判断缓存有没有生效——只能靠它来验证，不能靠推理。
    ///
    /// <b>只是诊断值，不是命中率</b>。一轮 agent 里上下文单调增长，最后一发的前缀正好是上一发的全部，
    /// 按构造就接近全命中；写交接那一发（供给形态不同、前缀不同）还会把这一对数整个覆写掉。
    /// 要报命中率用 <see cref="SessionCachedInput"/> / <see cref="SessionInput"/>
    /// </summary>
    public long LastCachedInput { get; private set; }
    
    /// <summary>
    /// 最近一次响应服务端报的当前 token 统计。
    /// </summary>
    public long TotalTokenCount { get; private set; }

    /// <summary>
    /// 最近一次响应里服务端报的思考（推理）token，0 表示不报或没有。
    /// 服务端通常把思考 token 算在输出里，单列一份是为了能看出"回复里有多少是思考"。
    /// </summary>
    public long LastReasoningTokens { get; private set; }

    /// <summary>
    /// 会话累计思考（推理）token。随本体持久化（见 <c>ChatSession.TotalReasoningTokens</c>），
    /// 切回会话时由 <see cref="RestoreFrom"/> 恢复。
    /// </summary>
    public long SessionReasoningTokens { get; private set; }

    /// <summary>本轮输出 token</summary>
    public long TurnOutput { get; private set; }

    /// <summary>会话累计输入 token</summary>
    public long SessionInput { get; private set; }

    /// <summary>
    /// 会话累计命中前缀缓存的输入 token，与 <see cref="SessionInput"/> 同源累加。
    /// 命中率由这两个数现算，是唯一能反映「这个会话到目前为止缓存到底省了多少」的口径。
    /// 随本体持久化（见 <c>ChatSession.TotalCachedInputTokens</c>）
    /// </summary>
    public long SessionCachedInput { get; private set; }

    /// <summary>会话累计输出 token</summary>
    public long SessionOutput { get; private set; }

    /// <summary>
    /// 输入框草稿的 token 估算（尚未发送，不计入累计）。
    /// <b>按需算</b>：只在上下文面板打开时数一次，不再跟着打字实时刷——
    /// 那是每次停顿整段全文重算一遍，换来的只是状态栏一个数字
    /// </summary>
    public int InputEstimate { get; set; }

    /// <summary>本轮请求次数（每次拿到一次响应用量计一次，含 0 token 的响应）</summary>
    public int TurnRequests { get; private set; }

    /// <summary>
    /// 会话累计请求次数，与 <see cref="SessionInput"/> 同源累加——每次响应就是一次请求。
    /// 随本体持久化（见 <c>ChatSession.TotalRequests</c>），切回会话时由 <see cref="RestoreFrom"/> 恢复
    /// </summary>
    public int SessionRequests { get; private set; }

    /// <summary>
    /// 开始新一轮：只清本轮，累计值保留
    /// </summary>
    public void BeginTurn()
    {
        TurnInput = 0;
        TurnOutput = 0;
        TurnCachedInput = 0;
        TurnRequests = 0;
    }

    /// <summary>
    /// 计入一次响应用量
    /// </summary>
    /// <param name="details">响应携带的用量</param>
    /// <returns>本次的增量（输入、输出、思考、命中），供调用方写回会话本体</returns>
    public (long Input, long Output, long Reasoning, long Cached) Add(UsageDetails details)
    {
        //输入消息（含 system、历史对话、用户消息）的 token 数
        long input = details.InputTokenCount ?? 0;
        //模型生成的回复 token 数（含推理 token）,不含历史
        long output = details.OutputTokenCount ?? 0;
        long reasoning = 0;
        //本次命中的输入。一次服务调用只报一份用量（见 StreamUsage），所以这一次读出来的就是这一发的
        long cached = ReadCachedTokens(details);
        if (input > 0)
        {
            LastInput = input;
            LastCachedInput = cached; //不报就归零,不能留着上一次的数冒充本次命中
            LastReasoningTokens = details.ReasoningTokenCount ?? 0; //同理:没报这次数就归零
            reasoning = LastReasoningTokens;
        }

        TotalTokenCount = details.TotalTokenCount??0;

        // 请求次数与 token 累计同源：流式里 provider 每个 chunk 都带 usage，
        // 这里只接受「一次服务调用」这一档（见 StreamUsage），所以一次调用即一次请求
        TurnRequests++;
        SessionRequests++;

        TurnInput += input;
        TurnOutput += output;
        TurnCachedInput += cached;
        SessionInput += input;
        SessionOutput += output;
        SessionCachedInput += cached;
        SessionReasoningTokens += reasoning;
        return (input, output, reasoning, cached);
    }

    /// <summary>
    /// 从用量的附加计数里找缓存命中数。各家键名不同（OpenAI 系是 <c>cached_tokens</c>，
    /// 经 MEAI 映射后还会再改一次名），所以按子串命中而不是写死键名；找不到就是 0。
    /// </summary>
    /// <param name="details">响应用量</param>
    /// <returns>命中的输入 token 数</returns>
    internal static long ReadCachedTokens(UsageDetails details)
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
    /// 计入一次响应用量并写回会话本体。累计值按增量加而不是整值覆盖：
    /// 群后台的旁路调用也往同一本体上加（见 <c>GroupBackground</c>），覆盖会把那部分抹掉
    /// </summary>
    /// <param name="details">响应携带的用量</param>
    /// <param name="session">会话本体</param>
    public void Record(UsageDetails details, ChatSession session)
    {
        (long input, long output, long reasoning, long cached) = Add(details);
        session.AccumulateUsage(input, output, reasoning, cached);
        // 占用、命中与本轮用量随本体持久化:切回会话不必等下一次响应,旁观的窗口(子会话)也只读得到本体
        session.LastInputTokens = LastInput;
        session.LastCachedInputTokens = LastCachedInput;
        session.LastTurnInputTokens = TurnInput;
        session.LastTurnOutputTokens = TurnOutput;
        session.LastTurnCachedInputTokens = TurnCachedInput;
    }

    /// <summary>
    /// 从会话本体恢复 <see cref="Record"/> 写下的那些数（响应用量不随消息持久化）。
    /// 本轮那两个数是驱动者逐块记的，调用方只在没在跑的时候（重开会话、旁观别人那一轮）调这个
    /// </summary>
    /// <param name="session">会话本体</param>
    public void RestoreFrom(ChatSession session)
    {
        SessionInput = session.TotalInputTokens;
        SessionOutput = session.TotalOutputTokens;
        SessionReasoningTokens = session.TotalReasoningTokens;
        SessionCachedInput = session.TotalCachedInputTokens;
        SessionRequests = session.TotalRequests;
        LastInput = session.LastInputTokens;
        LastCachedInput = session.LastCachedInputTokens;
        TurnInput = session.LastTurnInputTokens;
        TurnOutput = session.LastTurnOutputTokens;
        TurnCachedInput = session.LastTurnCachedInputTokens;
    }

    /// <summary>
    /// 压缩后重置占用段：历史已被记忆压缩文档替换，进度条不该再顶着压缩前的大数。
    /// 累计（会话花了多少）保留，只有「现在多满」这一段换成压缩后的占用——下一轮请求后由执行侧覆写。
    ///
    /// <b>会话本体上的占用字段一并更新</b>：交接请求本身也是一次记账（见
    /// <c>TurnDriver.RecordUsage</c>），它的输入是压缩前的全量历史，那个数早已写回本体并随会话头持久化；
    /// 只清内存会让旁观窗口与下次切回会话时的 <see cref="RestoreFrom"/> 又恢复成大数。
    /// </summary>
    /// <param name="session">压缩的那个会话，其占用字段会同步覆写</param>
    /// <param name="afterOccupancy">压缩后的占用（交接文档 + 保留消息 + 固定开销的估算，调用方现算）；
    /// 归零会显示成「会话像被清空」，留压缩前的数又像压缩没生效</param>
    public void ResetOccupancyAfterCompaction(ChatSession session, long afterOccupancy)
    {
        LastInput = Math.Max(0, afterOccupancy);
        EstimatedInput = 0;
        CompactedInput = 0;
        LastCachedInput = 0;
        LastReasoningTokens = 0;
        TotalTokenCount = 0;

        session.LastInputTokens = LastInput;
        session.LastCachedInputTokens = 0;
        session.LastTurnInputTokens = 0;
        session.LastTurnOutputTokens = 0;
        session.LastTurnCachedInputTokens = 0;
    }

    /// <summary>
    /// 全部归零（切换会话）
    /// </summary>
    public void Reset()
    {
        TurnInput = 0;
        TurnOutput = 0;
        LastInput = 0; //占用是「这个会话现在多满」,换会话必须清掉,否则会挂着上一个会话的数
        EstimatedInput = 0; //同理:它是另一半口径,留着会让新会话一开始就顶着旧会话的固定开销
        CompactedInput = 0;
        FixedOverhead = 0;
        LastCachedInput = 0;
        LastReasoningTokens = 0;
        TotalTokenCount = 0;
        SessionInput = 0;
        SessionOutput = 0;
        SessionCachedInput = 0;
        SessionReasoningTokens = 0;
        SessionRequests = 0;
        TurnRequests = 0;
    }

    /// <summary>
    /// 折成 k / M，保留一位小数。给状态栏那种寸土寸金的位置用。
    /// </summary>
    /// <param name="count">token 数</param>
    /// <returns>显示用字符串</returns>
    public static string Format(long count)
    {
        if (count >= 1_000_000) return $"{count / 1_000_000.0:0.#}M";
        return count >= 1000 ? $"{count / 1000.0:0.#}k" : count.ToString();
    }

    /// <summary>
    /// 确切数值（带千位分隔）。悬停面板里用——那里有地方，而缩写会把
    /// 「离下一道水位还有多远」这种要比大小的判断变模糊。
    /// </summary>
    /// <param name="count">token 数</param>
    /// <returns>显示用字符串</returns>
    public static string FormatExact(long count) => Format(count); //count.ToString("N0");
}
