using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 模型在工具循环里反复产出同样的思考、调用同一把工具且拿到同样的结果，本轮已中止。
/// 与 <see cref="RepetitionLoopException"/> 互补：那个抓单条流内的字符级复读，
/// 这个抓跨服务调用的整段重放（思考结束 → 调工具 → 又是一模一样的思考 → 又调同一把工具）。
/// </summary>
public sealed class ToolCallLoopException(string toolSummary, int rounds, string? modelName = null)
    : Exception($"{(modelName == null ? "模型" : $"模型 {modelName}")}连续 {rounds} 轮输出了完全相同的思考与工具调用（{toolSummary}），判定为退化循环，已自动中止本轮。");

/// <summary>
/// 盯一轮之内跨服务调用的退化循环：把每一发的思考正文、工具调用与工具结果合成一个签名，
/// 连续若干发签名完全相同就抛 <see cref="ToolCallLoopException"/>。
/// </summary>
/// <remarks>
/// 边界取 <see cref="ServiceCallStartedContent"/>（发请求前同步写入，不迟到），不用
/// <see cref="MessageBoundaryContent"/>（落盘回调会晚于下一发首条内容）。思考抽取走
/// <see cref="ThinkingBoundary"/> 这个全仓唯一分类入口，本地模型的 <c>&lt;think&gt;</c>
/// 内联思考因此同样被覆盖。工具调用签名刻意<b>不含 callId</b>——退化循环里每一发的 callId
/// 都不同，含它就永远认不出来；但<b>含工具结果</b>——结果还在变的轮询是合法等待，只有
/// 思考、调用、结果三者都原样重放才算退化。与 <see cref="RepetitionLoopWatch"/> 是两道独立的闸。
/// </remarks>
internal sealed class ToolCallLoopWatch
{
    /// <summary>连续多少发签名相同才判退化。允许两发相同（偶尔的正常重试），第三发才收口</summary>
    public const int RepeatThreshold = 3;

    /// <summary>异常里带上的工具摘要上限，避免整段文件内容打进日志与错误卡</summary>
    private const int ToolSummaryLimit = 160;

    private readonly StringBuilder _reasoning = new();
    private readonly List<string> _calls = new();
    private readonly List<string> _results = new();
    private readonly ThinkTagStreamParser _thinkParser = new();
    private readonly Action<string> _onText;
    private readonly Action<string> _onThinking;
    private readonly Action _onBreak;
    private readonly string? _modelName;
    private string? _lastSignature;
    private int _streak;

    /// <summary>
    /// 创建跨服务调用的退化循环看门狗
    /// </summary>
    /// <param name="modelName">模型名，仅用于异常提示</param>
    public ToolCallLoopWatch(string? modelName = null)
    {
        _modelName = modelName;
        // 流式路径每帧都过,回调缓存成字段,别每次 Observe 都分配三个委托
        _onText = _ => { };
        _onThinking = text => _reasoning.Append(text);
        _onBreak = () => { };
    }

    /// <summary>
    /// 观察一条流式内容
    /// </summary>
    /// <param name="content">框架流里的内容</param>
    /// <exception cref="ToolCallLoopException">认出跨服务调用的退化循环</exception>
    public void Observe(AIContent content)
    {
        // 思考抽取复用全仓唯一分类入口：结构化的 TextReasoningContent 与本地模型的
        // <think> 内联思考都归到 onThinking；正文丢弃。SCS 那一支由 Dispatch 先冲刷扣留内容，
        // 再由下面自己的分支结算——两者顺序无副作用。
        ThinkingBoundary.Dispatch(content, _thinkParser, _onText, _onThinking, _onBreak);

        switch (content)
        {
            case ServiceCallStartedContent:
                Settle();
                break;
            case StreamRestartContent:
                // 这一发整段重来：攒下的内容作废，连续计数也归零（上一发的半截状态不作数）
                Reset();
                _lastSignature = null;
                _streak = 0;
                break;
            case FunctionCallContent call:
                _calls.Add(SignatureOf(call));
                break;
            case FunctionResultContent result:
                // 本发的工具结果：与落盘、投影同一口径取正文（刚产出是 string，回放是 JsonElement）
                _results.Add(HashOf(ImagePreview.ResultText(result) ?? string.Empty));
                break;
        }
    }

    private void Settle()
    {
        // 没有思考正文（加密思考、关思考）或无工具调用时，签名不足以判退化：
        // 空思考配同一把工具是后台轮询的常见形状；这类会话由 RepetitionLoopWatch 兜单发复读
        string signature = _reasoning.Length > 0 && _calls.Count > 0 ? SignatureOfRound() : string.Empty;
        string summary = _calls.Count > 0 ? _calls[0] : string.Empty;
        Reset();

        if (signature.Length == 0)
        {
            _lastSignature = null;
            _streak = 0;
            return;
        }

        _streak = signature == _lastSignature ? _streak + 1 : 1;
        _lastSignature = signature;
        if (_streak >= RepeatThreshold) throw new ToolCallLoopException(Summarize(summary), _streak, _modelName);
    }

    private string SignatureOfRound()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(_reasoning.ToString()));
        return $"{Convert.ToHexString(hash)}|{string.Join(";", _calls)}|{string.Join(";", _results)}";
    }

    private static string SignatureOf(FunctionCallContent call)
    {
        if (call.Arguments is not { Count: > 0 }) return call.Name + "()";
        // 按 key 排序后整体序列化：消除 JSON 对象字段顺序漂移；走 ToolJson.Arguments 的
        // source-gen 口径，与落盘、审批提示同一套参数写法，又没有 AOT/trimming 警告
        Dictionary<string, object?> ordered = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> pair in call.Arguments.OrderBy(p => p.Key, StringComparer.Ordinal))
            ordered[pair.Key] = pair.Value;
        return call.Name + "(" + JsonSerializer.Serialize(ordered, ToolJson.Arguments) + ")";
    }

    private static string HashOf(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash)[..8];
    }

    private static string Summarize(string summary) =>
        summary.Length <= ToolSummaryLimit ? summary : summary[..ToolSummaryLimit] + "…";

    private void Reset()
    {
        _reasoning.Clear();
        _calls.Clear();
        _results.Clear();
        _thinkParser.Reset();
    }
}
