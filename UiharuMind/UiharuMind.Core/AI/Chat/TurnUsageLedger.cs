/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// token 账本：本轮用量、会话累计用量与输入框估算，以及它们的显示文本格式化。
/// 只记账不落盘——会话本体的累计字段由调用方按 <see cref="Add"/> 返回的增量自行写回，
/// 账本因此与存储无关，可直接单测。
/// </summary>
public sealed class TurnUsageLedger
{
    /// <summary>本轮输入 token（一轮多次工具往返时为各次之和，是成本视角）</summary>
    public long TurnInput { get; private set; }

    /// <summary>
    /// 最近一次响应的输入 token。这是**上下文占用**的口径——服务端实际吃进去多少，
    /// 压缩之后的真实结果，比任何本地估算都准。不能拿 <see cref="TurnInput"/> 顶替：
    /// 那是本轮累加值，一轮十几次工具往返能累到四十几万，与占用无关。
    /// </summary>
    public long LastInput { get; private set; }

    /// <summary>当前模型的上下文窗口，0 表示未知（占用段整段省略）</summary>
    public int ContextLength { get; set; }

    /// <summary>
    /// 最近一次响应里命中前缀缓存的输入 token，0 表示未命中或服务端不报。
    /// 服务端不报这个数的话就无从判断缓存有没有生效——只能靠它来验证，不能靠推理。
    /// </summary>
    public long LastCachedInput { get; private set; }

    /// <summary>本轮输出 token</summary>
    public long TurnOutput { get; private set; }

    /// <summary>会话累计输入 token</summary>
    public long SessionInput { get; private set; }

    /// <summary>会话累计输出 token</summary>
    public long SessionOutput { get; private set; }

    /// <summary>输入框文本的 token 估算（尚未发送，不计入累计）</summary>
    public int InputEstimate { get; set; }

    /// <summary>
    /// 开始新一轮：只清本轮，累计值保留
    /// </summary>
    public void BeginTurn()
    {
        TurnInput = 0;
        TurnOutput = 0;
    }

    /// <summary>
    /// 计入一次响应用量
    /// </summary>
    /// <param name="details">响应携带的用量</param>
    /// <returns>本次的增量，供调用方写回会话本体</returns>
    public (long Input, long Output) Add(UsageDetails details)
    {
        long input = details.InputTokenCount ?? 0;
        long output = details.OutputTokenCount ?? 0;
        if (input > 0)
        {
            LastInput = input;
            LastCachedInput = ReadCachedTokens(details); //不报就归零,不能留着上一次的数冒充本次命中
        }

        TurnInput += input;
        TurnOutput += output;
        SessionInput += input;
        SessionOutput += output;
        return (input, output);
    }

    /// <summary>
    /// 从用量的附加计数里找缓存命中数。各家键名不同（OpenAI 系是 <c>cached_tokens</c>，
    /// 经 MEAI 映射后还会再改一次名），所以按子串命中而不是写死键名；找不到就是 0。
    /// </summary>
    /// <param name="details">响应用量</param>
    /// <returns>命中的输入 token 数</returns>
    internal static long ReadCachedTokens(UsageDetails details)
    {
        if (details.AdditionalCounts == null) return 0;

        foreach (KeyValuePair<string, long> pair in details.AdditionalCounts)
        {
            if (pair.Key.Contains("cach", StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }

        return 0;
    }

    /// <summary>
    /// 从会话本体恢复累计值（响应用量不随消息持久化，累计值记在本体上）
    /// </summary>
    /// <param name="input">累计输入</param>
    /// <param name="output">累计输出</param>
    /// <param name="lastInput">最近一次响应的输入 token（上下文占用），未知传 0</param>
    public void RestoreSession(long input, long output, long lastInput = 0)
    {
        SessionInput = input;
        SessionOutput = output;
        LastInput = lastInput;
    }

    /// <summary>
    /// 全部归零（切换会话）
    /// </summary>
    public void Reset()
    {
        TurnInput = 0;
        TurnOutput = 0;
        LastInput = 0; //占用是「这个会话现在多满」,换会话必须清掉,否则会挂着上一个会话的数
        LastCachedInput = 0;
        SessionInput = 0;
        SessionOutput = 0;
    }

    /// <summary>
    /// 状态栏文本：「占用/上限  ≈输入估算」，无数据的段落整段省略。
    ///
    /// 刻意只留这两段。它紧挨发送按钮、位置很窄，四段堆进去会挤成一串看不出量级的数字；
    /// 本轮用量与会话累计都在悬停面板里，那里有标签、读得懂。
    /// </summary>
    public string Text
    {
        get
        {
            StringBuilder sb = new();
            // 上限未知(还没选模型)时也要显示占用:那个数是从会话本体恢复出来的,
            // 「这个会话现在有多大」跟当前选没选模型无关,整段藏掉等于凭空少了一条信息
            if (LastInput > 0)
            {
                sb.Append(ContextLength > 0 ? $"{Format(LastInput)}/{Format(ContextLength)}" : Format(LastInput));
            }

            if (InputEstimate > 0)
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append($"≈{Format(InputEstimate)}");
            }

            return sb.ToString();
        }
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
