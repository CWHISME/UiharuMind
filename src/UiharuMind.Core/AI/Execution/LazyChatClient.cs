/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.AI.Execution;
/// 惰性模型客户端:构建 agent 时不要求模型就绪,每次请求时解析全局当前模型。
/// 使会话历史的加载/回放与模型状态解耦(重启后无模型也能看历史),
/// 顶栏切换模型后无需重建 agent,下一次请求自动生效。
/// </summary>
public class LazyChatClient : IChatClient
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>流式中途被服务端掐断(HttpIOException: ResponseEnded)的重试次数上限。</summary>
    private const int StreamingRetryMaxAttempts = 6;

    /// <summary>断线重试退避基数:1s、2s、4s…封顶 <see cref="StreamingRetryMaxDelay"/></summary>
    private static readonly TimeSpan StreamingRetryBaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>断线重试退避封顶。连续断线多半是服务端/网络不稳,等太久的收益有限</summary>
    private static readonly TimeSpan StreamingRetryMaxDelay = TimeSpan.FromSeconds(8);

    private readonly Func<ModelRunningData?>? _sessionModelSource; //会话级模型来源,优先于全局当前模型

    public LazyChatClient(Func<ModelRunningData?>? sessionModelSource = null)
    {
        _sessionModelSource = sessionModelSource;
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChatMessage> messageList = AsList(messages);
        LlmRequestContext.PendingReasoningSource = () => CollectAssistantReasoning(messageList);

        var (client, model) = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (ReasoningRequirementErrorHandler.IsRequirementError(e))
        {
            // 服务端要求每条助手消息都带思考:记下这个模型,重发一次(请求体会按新配置重新改写)
            if (!TryLearnReasoningRequirement(model)) throw;
            Log.Warning($"server requires reasoning on every earlier assistant message " +
                        $"(model '{model.ModelInfo.ModelId}'); marked for this model, retrying once.");
            response = await client.GetResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false);
        }

        HashSet<string>? toolNames = CollectToolNames(options);
        if (toolNames == null) return response;
        foreach (ChatMessage message in response.Messages)
        {
            message.Contents = RecoverTextToolCalls(message.Contents, toolNames);
        }

        return response;
    }

    /// <summary>
    /// 流式请求当前模型。中途断线整段重发（见 <see cref="StreamWithRetryAsync"/>）；
    /// 用量只认流尾那一份——有的服务商一次调用报两遍累计用量，逐条记账会翻倍（见 <see cref="StreamUsage"/>）
    /// </summary>
    /// <param name="messages">请求消息</param>
    /// <param name="options">请求选项</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>流式更新，至多带一份用量</returns>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        StreamUsage.KeepLast(StreamWithRetryAsync(messages, options, cancellationToken), cancellationToken);

    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithRetryAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> messageList = AsList(messages);
        LlmRequestContext.PendingReasoningSource = () => CollectAssistantReasoning(messageList);

        var (client, model) = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string>? toolNames = CollectToolNames(options);

        // 流式中途被服务端掐断(HttpIOException: ResponseEnded)时重发整个请求再试几次。
        // 这类失败发生在响应头已到手、body 读到一半的时候,SDK 的限流/瞬时重试看不见它,
        // 不在这里兜底就一路冒到 TurnDriver,整轮 agent 任务直接作废。
        // 正常流以 [DONE] 收尾,不会抛 HttpIOException,自然走不到重试;
        // 例外是 [DONE] 收尾却只有思考、没有 finish_reason 的截断回复(见 StreamCompletionWatch),同样重试。
        // 重试代价:若断线前已向上层吐过完整的工具调用,重发会让同一调用被框架再执行一次——
        // 好在工具调用通常出现在流中后段,断线多发在思考/等待的停顿期,此时尚无新调用产出。
        bool maxTokensFixed = false; // 服务端拒 max_tokens 的自愈只做一次
        bool reasoningRequirementLearned = false; // 「每条助手消息都要带思考」的自愈只做一次
        for (int attempt = 1; ; attempt++)
        {
            bool retryable = false;
            await foreach (var (update, failure) in StreamOnceAsync(
                               client, messageList, options, toolNames, model.ModelName, cancellationToken).ConfigureAwait(false))
            {
                if (failure != null)
                {
                    // 服务端拒绝 max_tokens(响应头阶段的 400):不走断线退避——修好配置后立即整段重发一次。
                    // 重发是一次全新的 SDK 调用,请求体由 OpenAICompatibleRequestPolicy 重新改写,新值真正生效。
                    // 自愈只做一次;修不了(非远程配置型模型)或重发仍被拒 → 直接抛给上层,不空耗断线的重试次数。
                    if (MaxTokensErrorHandler.TryGetUpperBound(failure, out int upperBound))
                    {
                        if (maxTokensFixed || !TryFixMaxTokens(model, upperBound))
                        {
                            ExceptionDispatchInfo.Capture(failure).Throw();
                        }

                        maxTokensFixed = true;
                        Log.Warning($"max_tokens rejected by the server (declared upper bound {upperBound}); " +
                                    "model config corrected, retrying this stream once.");
                        retryable = true;
                        break;
                    }

                    // 服务端要求历史里每条助手消息都带思考:记下这个 ModelId,整段重发一次。
                    // 同 max_tokens 一样只自愈一次——记过之后请求体已经带上字段,再被拒就是别的问题
                    if (ReasoningRequirementErrorHandler.IsRequirementError(failure))
                    {
                        if (reasoningRequirementLearned || !TryLearnReasoningRequirement(model))
                        {
                            ExceptionDispatchInfo.Capture(failure).Throw();
                        }

                        reasoningRequirementLearned = true;
                        Log.Warning($"server requires reasoning on every earlier assistant message " +
                                    $"(model '{model.ModelInfo.ModelId}'); marked for this model, retrying this stream once.");
                        retryable = true;
                        break;
                    }

                    retryable = true;
                    if (attempt >= StreamingRetryMaxAttempts)
                    {
                        Log.Warning($"[stream] response dropped mid-stream, giving up after {attempt} attempts: {failure.Message}");
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    }

                    TimeSpan delay = StreamingRetryDelay(attempt);
                    Log.Warning($"[stream] response dropped mid-stream ({failure.Message}); " +
                                $"retry {attempt}/{StreamingRetryMaxAttempts} in {delay.TotalSeconds:0.#}s.");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    break;
                }

                yield return update!;
            }

            if (!retryable) yield break;
        }
    }

    /// <summary>
    /// 把远程模型配置的 <see cref="BaseRemoteModelConfig.MaxTokens"/> 回退为服务端声明的合法上限并落盘。
    /// 配置中心里就是同一份对象:改完无需额外同步,下次请求的 <c>GetExtraParams</c> 直接按新值发。
    /// 本地模型没有可调的输出预算配置(报错属服务端限制),返回 false 交给上层处理。
    /// </summary>
    private static bool TryFixMaxTokens(ModelRunningData model, int upperBound)
    {
        if (model.ModelInfo is not RemoteModelInfo remote) return false;
        BaseRemoteModelConfig config = remote.Config;
        if (config.MaxTokens == upperBound) return true; // 已是合法值
        config.MaxTokens = upperBound;
        try
        {
            RemoteModelSettingConfig.Current.Save();
            return true;
        }
        catch (Exception e)
        {
            // 内存已修好、本次重发能成功;落盘失败只影响重启后,不阻塞自愈
            Log.Warning($"max_tokens correction (MaxTokens={upperBound}) could not be persisted: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// 记下「这个模型要求历史里每条助手消息都带思考」并落盘，供下一次重发使用。
    /// 非远程配置型模型（本地）改不了，返回 false 交上层照原样抛错。
    /// </summary>
    private static bool TryLearnReasoningRequirement(ModelRunningData model)
    {
        if (model.ModelInfo is not RemoteModelInfo remote) return false;
        if (!remote.Config.LearnRequireReasoningOnEveryAssistantMessage()) return false;
        try
        {
            RemoteModelSettingConfig.Current.Save();
        }
        catch (Exception e)
        {
            // 内存已记下、本次重发能成功;落盘失败只影响重启后,不阻塞自愈
            Log.Warning($"reasoning requirement (ModelId={remote.ModelId}) could not be persisted: {e.Message}");
        }

        return true;
    }

    /// <summary>
    /// 单次流式枚举。中途断线(HttpIOException)、收尾判为截断或服务端拒参数(400 max_tokens)时不向外抛,
    /// 而是产出 (null, 异常) 让上层决定重试;正常产出 (update, null)。手动驱动 MoveNextAsync 是为了把 yield 从带 catch
    /// 的 try 里挪出来 (C# 不允许在带 catch 的 try 内 yield)。
    /// </summary>
    private async IAsyncEnumerable<(ChatResponseUpdate? Update, Exception? Failure)> StreamOnceAsync(
        IChatClient client, IReadOnlyList<ChatMessage> messages, ChatOptions? options, HashSet<string>? toolNames,
        string? modelName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 文本工具调用恢复:GLM 线格式的调用可能以纯文本漏进正文或思考通道
        // (服务端未翻译成结构化 tool_calls 时),在此转回 FunctionCallContent,
        // 使上层框架的函数调用循环照常执行。两通道各持解析器,互不串流。
        TextToolCallStreamParser? textParser = toolNames == null ? null : new TextToolCallStreamParser(toolNames);
        TextToolCallStreamParser? reasoningParser = toolNames == null ? null : new TextToolCallStreamParser(toolNames);

        // [诊断] 统计思考/正文各收到多少字符。流完整收完但界面没有正文时,
        // 这条日志能区分"服务端根本没发正文"与"发了但被上层丢了"
        int reasoningChars = 0;
        int textChars = 0;
        StreamCompletionWatch watch = new();
        // 复读不归重试管：直接抛出，本轮按失败收口（已生成的部分照常落盘）
        RepetitionLoopWatch loopWatch = new(modelName);

        IAsyncEnumerator<ChatResponseUpdate> enumerator = client
            .GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                ChatResponseUpdate? update = null;
                Exception? failure = null;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                    update = enumerator.Current;
                }
                catch (HttpIOException e) when (!cancellationToken.IsCancellationRequested)
                {
                    failure = e;
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested && MaxTokensErrorHandler.IsMaxTokensError(e))
                {
                    // 响应头阶段的 400 在此转成交付形式,交上层做一次性自愈重发(见 StreamWithRetryAsync)
                    failure = e;
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested &&
                                          ReasoningRequirementErrorHandler.IsRequirementError(e))
                {
                    // 同上:缺思考字段的 400 也交给上层,记下模型后重发
                    failure = e;
                }

                if (failure != null)
                {
                    yield return (null, failure);
                    yield break;
                }

                // 能走到这里,update 必已赋值(failure 分支已退出);显式断言收窄可空性
                if (update is null) throw new UnreachableException();

                if (textParser == null)
                {
                    DropEmptyTextContents(update);
                    watch.Observe(update);
                    loopWatch.Observe(update);
                    yield return (update, null);
                    continue;
                }

                List<AIContent> rebuilt = new(update.Contents.Count);
                foreach (AIContent content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent { Text.Length: > 0 } tc:
                            textChars += tc.Text.Length;
                            Append(rebuilt, textParser.Feed(tc.Text), isReasoning: false);
                            break;
                        case TextReasoningContent { Text.Length: > 0 } rc:
                            reasoningChars += rc.Text.Length;
                            Append(rebuilt, reasoningParser!.Feed(rc.Text), isReasoning: true);
                            break;
                        default:
                            //空正文/空思考不留:它们只会把思考段切碎,详见 ChatContentNormalizer
                            if (!ChatContentNormalizer.IsEmptyTextLike(content)) rebuilt.Add(content);
                            break;
                    }
                }

                update.Contents = rebuilt;
                watch.Observe(update);
                loopWatch.Observe(update);
                yield return (update, null);
            }
        }

        if (textParser != null)
        {
            Log.Debug($"[stream] assistant reply ended: reasoning={reasoningChars:N0} chars, text={textChars:N0} chars, " +
                      $"finish_reason={watch.FinishReason ?? "(none)"}.");

            List<AIContent> tail = new();
            Append(tail, textParser.Flush(), isReasoning: false);
            Append(tail, reasoningParser!.Flush(), isReasoning: true);
            if (tail.Count > 0)
            {
                ChatResponseUpdate tailUpdate = new(ChatRole.Assistant, tail);
                watch.Observe(tailUpdate);
                yield return (tailUpdate, null);
            }
        }

        // 网关断了上游仍会补 [DONE] 正常收尾，传输层看不出来；按断线处理，交给上层重试
        if (watch.IsCutOff)
        {
            yield return (null, new HttpIOException(HttpRequestError.ResponseEnded,
                "stream ended without finish_reason, text or tool calls (reply was cut off upstream)"));
        }
    }

    /// <summary>断线重试退避:以 <see cref="StreamingRetryBaseDelay"/> 为基数的指数退避,封顶 <see cref="StreamingRetryMaxDelay"/></summary>
    /// <param name="attempt">当前是第几次尝试(从 1 开始)</param>
    private static TimeSpan StreamingRetryDelay(int attempt)
    {
        double scaled = StreamingRetryBaseDelay.TotalSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(scaled, StreamingRetryMaxDelay.TotalSeconds));
    }

    /// <summary>
    /// 丢掉增量里的空正文/空思考。必须赶在框架把增量合并成消息<b>之前</b>做:
    /// 合并只并连续同类型的内容,空正文一格一格插在思考中间,合并后就是几十段碎片
    /// (详见 <see cref="ChatContentNormalizer"/>)。
    /// </summary>
    /// <param name="update">流式增量(就地修改)</param>
    private static void DropEmptyTextContents(ChatResponseUpdate update)
    {
        IList<AIContent> contents = update.Contents;
        int kept = 0;
        foreach (AIContent content in contents)
        {
            if (!ChatContentNormalizer.IsEmptyTextLike(content)) kept++;
        }

        if (kept == contents.Count) return; //绝大多数增量本就干净,不重建列表

        List<AIContent> rebuilt = new(kept);
        foreach (AIContent content in contents)
        {
            if (!ChatContentNormalizer.IsEmptyTextLike(content)) rebuilt.Add(content);
        }

        update.Contents = rebuilt;
    }

    /// <summary>历史消息可能是一次性的枚举,重复枚举(转发给底层客户端+扫描思考正文)前先落地一份</summary>
    private static IReadOnlyList<ChatMessage> AsList(IEnumerable<ChatMessage> messages) =>
        messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

    /// <summary>
    /// 从历史消息里按<b>助手消息出现顺序</b>收集思考正文,供 <see cref="LlmRequestContext.PendingReasoningSource"/>
    /// 使用——转发给底层客户端之前,消息里的 <see cref="TextReasoningContent"/> 还在,过了这一层就没处找了。
    ///
    /// 按序而不是按 tool_call id:没有工具调用的普通回复同样要回传(网关按「每一条更早的助手消息」
    /// 校验),而它身上没有 id 可挂。返回的列表与请求里的助手消息一一对应,没有思考的位置留 null;
    /// 一条都没有时返回 null,让改写整段跳过。
    ///
    /// 一条消息里可能躺着多段思考(老会话是逐 chunk 落下的碎片),要拼齐了发回去——
    /// 只取其中一段等于把思考截成一句话,接口那边照样对不上。只有一段时直接用它,不另拼一份。
    /// </summary>
    internal static IReadOnlyList<string?>? CollectAssistantReasoning(IReadOnlyList<ChatMessage> messages)
    {
        List<string?>? collected = null;
        foreach (ChatMessage message in messages)
        {
            if (message.Role != ChatRole.Assistant) continue;
            (collected ??= []).Add(JoinReasoning(message.Contents));
        }

        return collected?.Exists(x => x != null) == true ? collected : null;
    }

    /// <summary>拼齐一条消息里的多段思考;没有思考正文时返回 null</summary>
    private static string? JoinReasoning(IList<AIContent> contents)
    {
        string? single = null;
        StringBuilder? joined = null;
        foreach (AIContent content in contents)
        {
            switch (content)
            {
                case TextReasoningContent { Text.Length: > 0 } rc when single == null:
                    single = rc.Text;
                    break;
                case TextReasoningContent { Text.Length: > 0 } rc:
                    (joined ??= new StringBuilder(single)).Append(rc.Text);
                    break;
            }
        }

        return single == null ? null : joined?.ToString() ?? single;
    }

    /// <summary>
    /// 请求带工具时收集工具名集合(恢复器的启用条件与命中闸);无工具返回 null
    /// </summary>
    private static HashSet<string>? CollectToolNames(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools) return null;
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (AITool tool in tools)
        {
            if (!string.IsNullOrEmpty(tool.Name)) names.Add(tool.Name);
        }

        return names.Count > 0 ? names : null;
    }

    private static void Append(List<AIContent> target,
        (string Text, List<FunctionCallContent> Calls) parsed, bool isReasoning)
    {
        if (parsed.Text.Length > 0)
        {
            target.Add(isReasoning ? new TextReasoningContent(parsed.Text) : new TextContent(parsed.Text));
        }

        target.AddRange(parsed.Calls);
    }

    /// <summary>非流式响应的同款恢复(每条消息独立解析并冲刷)</summary>
    private static IList<AIContent> RecoverTextToolCalls(IList<AIContent> contents, HashSet<string> toolNames)
    {
        TextToolCallStreamParser textParser = new(toolNames);
        TextToolCallStreamParser reasoningParser = new(toolNames);
        List<AIContent> rebuilt = new(contents.Count);
        foreach (AIContent content in contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } tc:
                    Append(rebuilt, textParser.Feed(tc.Text), isReasoning: false);
                    break;
                case TextReasoningContent { Text.Length: > 0 } rc:
                    Append(rebuilt, reasoningParser.Feed(rc.Text), isReasoning: true);
                    break;
                default:
                    if (!ChatContentNormalizer.IsEmptyTextLike(content)) rebuilt.Add(content);
                    break;
            }
        }

        Append(rebuilt, textParser.Flush(), isReasoning: false);
        Append(rebuilt, reasoningParser.Flush(), isReasoning: true);
        return rebuilt;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        // 与 ResolveAsync 同一优先级:会话绑定模型优先,否则框架读到的能力元数据来自另一个模型
        ModelRunningData? model = _sessionModelSource?.Invoke() ?? LlmManager.Instance.CurrentRunningModel;
        return model?.ChatClient?.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        // 不持有底层客户端所有权
    }

    /// <summary>候选模型（会话钉选优先）的处置</summary>
    internal enum SessionCandidateDecision
    {
        /// <summary>直接用它（运行中、正在加载、远程可拉起）</summary>
        UseCandidate,
        /// <summary>回落全局（未运行的本地钉选，名字保留）</summary>
        UseGlobal,
        /// <summary>就地报错，不空等 30 秒</summary>
        FailFast,
    }

    /// <summary>
    /// 候选模型的处置：运行中/加载中/已就绪用它；远程未起用它（拉起后等就绪）；
    /// 本地未起回落全局；回无可回就地报错。纯函数，可单测。
    /// </summary>
    /// <param name="candidate">会话钉选优先解析出的候选</param>
    /// <param name="global">全局当前模型</param>
    internal static SessionCandidateDecision DecideCandidate(ModelRunningData candidate, ModelRunningData? global)
    {
        if (candidate.IsRunning || candidate.IsLoading || candidate.ChatClient != null)
            return SessionCandidateDecision.UseCandidate;
        if (candidate.IsRemoteModel) return SessionCandidateDecision.UseCandidate;
        if (global != null && !ReferenceEquals(global, candidate)) return SessionCandidateDecision.UseGlobal;
        return SessionCandidateDecision.FailFast;
    }

    /// <summary>
    /// 按与请求同一条判据解析出<b>真正会发请求的那个模型</b>：钉选的能跑就用它，
    /// 本地未起的钉选回落全局，回无可回时为 null（或那个注定起不来的候选）。
    ///
    /// 旁路请求（写交接文档）要用它定上下文上限。直接读 <c>ChatSession.ChatModelRunningData</c> 不够——
    /// 那是「钉着的名字」，不是「发请求的那个」：会话钉着一个没在跑的本地模型时它照样返回那个模型，
    /// 而它的 <c>ContextLength</c> 会落到本地兜底 8192。拿这个数去算水位与篇幅上限，
    /// 等于按一个根本不发请求的窗口在算：水位每轮都到，请求却发给另一个 128k 的模型。
    ///
    /// 纯函数、无副作用（不碰 <see cref="ResolveAsync"/> 那条会拉起模型的路径），可单测。
    /// </summary>
    /// <param name="candidate">会话钉选优先解析出的候选</param>
    /// <param name="global">全局当前模型</param>
    /// <returns>会真正发请求的那个模型；一个都指望不上时为 null</returns>
    public static ModelRunningData? ServingModel(ModelRunningData? candidate, ModelRunningData? global)
    {
        if (candidate == null) return global;
        return DecideCandidate(candidate, global) == SessionCandidateDecision.UseGlobal ? global : candidate;
    }

    /// <summary>
    /// 解析当前模型客户端与其归属;远程模型启动中时限时等待就绪
    /// </summary>
    private async Task<(IChatClient Client, ModelRunningData Model)> ResolveAsync(CancellationToken cancellationToken)
    {
        // 会话绑定的模型优先(如识图技能为临时会话解析的视觉模型),否则用全局当前模型
        ModelRunningData? model = _sessionModelSource?.Invoke() ?? LlmManager.Instance.CurrentRunningModel;
        // 无模型时按偏好自动选一个(优先运行中/收藏/远程);已有模型但远程未启动时顺带拉起。
        // 必须走会写回全局的那个重载:传局部变量的 ref 只会填上局部变量,
        // 全局当前模型仍是空——顶栏因此不跟着变,token 统计也拿不到模型与上下文上限
        if (model == null)
        {
            LlmManager.Instance.TryCheckModelRunning(false);
            model = LlmManager.Instance.CurrentRunningModel;
        }
        if (model == null) throw new InvalidOperationException("Model is not running.");

        // 钉选命中但从未启动（全局未选时）：远程顺手拉起后等就绪；本地一次只能跑一个，
        // 未运行的本地钉选回落全局（名字保留），回无可回就地报错、不空等 30 秒
        ModelRunningData? global = LlmManager.Instance.CurrentRunningModel;
        switch (DecideCandidate(model, global))
        {
            case SessionCandidateDecision.UseGlobal:
                model = global!;
                break;
            case SessionCandidateDecision.FailFast:
                throw new InvalidOperationException($"Model '{model.ModelName}' is not running.");
        }

        if (model.IsRemoteModel) LlmManager.Instance.EnsureModelStarted(model);

        DateTimeOffset deadline = DateTimeOffset.Now + ReadyTimeout;
        while (model.ChatClient == null && DateTimeOffset.Now < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return (model.ChatClient ?? throw new InvalidOperationException("Model is not running."), model);
    }
}