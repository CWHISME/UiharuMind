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
/// 一条消息里的<b>思考段</b>：连续下发的思考增量属于同一段，被正文或工具调用隔开就是两段
/// （思考与正文交替下发的模型，一条消息能有好几段）。
///
/// <para><b>段首空白不计</b>。界面不为纯空白开卡（实测 deepseek 会先发一个孤立空格，
/// 照建就是一张「1 字 · 1 字/秒」的假卡，见 <c>ConversationTranscript.AppendThinking</c>），
/// 要是存档这一侧照全额计字，两边的长度就会差这几个空格——回放认领时那张卡看着像
/// 「认错了消息」，只好放弃耗时与字数。所以这条口径收在这里，四个消费方（落盘的字数与段、
/// 渲染的卡、思考卡与历史消息的配对、回放认领的比较）都从这一处取。</para>
///
/// 段中、句中的空白照计：那是真内容。
/// </summary>
public static class ThinkingSegments
{
    /// <summary>
    /// 各段字数，按出现顺序；段首空白已扣掉，段中照计。
    /// </summary>
    /// <param name="message">一条历史消息（只读）</param>
    /// <returns>思考段长度；没有思考时为空的列表</returns>
    public static List<int> LengthsOf(ChatMessage message)
    {
        List<int> lengths = new();
        ThinkTagStreamParser? parser = null;
        int current = 0;
        bool open = false;

        void CloseRun()
        {
            if (!open) return;
            lengths.Add(current);
            current = 0;
            open = false;
        }

        void Feed(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!open)
            {
                text = text.TrimStart();
                if (text.Length == 0) return;
                open = true;
            }

            current += text.Length;
        }

        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextReasoningContent reasoning:
                    Feed(reasoning.Text);
                    break;
                // 本地/部分远程模型把 <think> 混在正文流里：解析出来的每一段各算一段
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    parser ??= new ThinkTagStreamParser();
                    parser.Feed(text.Text, _ => CloseRun(), Feed);
                    break;
                // 段边界问的是同一份名单（ThinkingBoundary.EndsThinking）：名单不一致的话，
                // 同一条消息会被切出与卡片对不上的段数——空正文、用量、子会话通知都不算收口
                case var ending when ThinkingBoundary.EndsThinking(ending):
                    CloseRun();
                    //段边界之后是下一段的开头:开标签重新算数（与流式侧同一口径）
                    parser?.Reset();
                    break;
            }
        }

        if (parser != null) parser.Complete(_ => CloseRun(), Feed);
        CloseRun();
        return lengths;
    }

    /// <summary>一条消息的思考字数合计（各段之和，段首空白不计）</summary>
    /// <param name="message">一条历史消息</param>
    /// <returns>思考字数；没有思考为 0</returns>
    public static long TotalLengthOf(ChatMessage message)
    {
        long total = 0;
        foreach (int length in LengthsOf(message)) total += length;
        return total;
    }
}
