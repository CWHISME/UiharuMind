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
    /// <summary>本轮输入 token</summary>
    public long TurnInput { get; private set; }

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
        TurnInput += input;
        TurnOutput += output;
        SessionInput += input;
        SessionOutput += output;
        return (input, output);
    }

    /// <summary>
    /// 从会话本体恢复累计值（响应用量不随消息持久化，累计值记在本体上）
    /// </summary>
    /// <param name="input">累计输入</param>
    /// <param name="output">累计输出</param>
    public void RestoreSession(long input, long output)
    {
        SessionInput = input;
        SessionOutput = output;
    }

    /// <summary>
    /// 全部归零（切换会话）
    /// </summary>
    public void Reset()
    {
        TurnInput = 0;
        TurnOutput = 0;
        SessionInput = 0;
        SessionOutput = 0;
    }

    /// <summary>
    /// 显示文本：「≈输入估算  ↑本轮输入 ↓本轮输出 (会话累计)」，无数据的段落整段省略
    /// </summary>
    public string Text
    {
        get
        {
            StringBuilder sb = new();
            if (InputEstimate > 0) sb.Append($"≈{Format(InputEstimate)}");
            if (TurnInput + TurnOutput > 0)
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append($"↑{Format(TurnInput)} ↓{Format(TurnOutput)}");
            }

            if (SessionInput + SessionOutput > 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append($"({Format(SessionInput + SessionOutput)})");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 万以上折成 k，保留一位小数
    /// </summary>
    /// <param name="count">token 数</param>
    /// <returns>显示用字符串</returns>
    public static string Format(long count) =>
        count >= 10000 ? $"{count / 1000.0:0.#}k" : count.ToString();
}
