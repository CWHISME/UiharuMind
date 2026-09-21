/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using Microsoft.ML.Tokenizers;

namespace UiharuMind.Core.AI;

/// <summary>
/// 文本 token 估算。统一用 o200k_base(GPT-4o 系)编码作近似——
/// 各家模型分词各异,此处只求量级参考。首次使用加载词表(数十毫秒级),故延迟初始化,
/// 调用方应避免在 UI 线程首触。
/// </summary>
public static class LlmTokenizer
{
    private static readonly Lazy<Tokenizer> _tokenizer =
        new(() => TiktokenTokenizer.CreateForEncoding("o200k_base"));

    /// <summary>
    /// 估算文本的 token 数
    /// </summary>
    /// <param name="text">文本</param>
    /// <returns>token 数,空文本为 0</returns>
    public static int CountTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : _tokenizer.Value.CountTokens(text);
    }

    /// <summary>
    /// 取开头不超过给定 token 数的一段文本
    /// </summary>
    /// <param name="text">原文</param>
    /// <param name="maxTokens">token 上限</param>
    /// <returns>截断位置的字符下标;整段都装得下时返回 text.Length</returns>
    public static int GetPrefixLengthByTokens(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text) || maxTokens <= 0) return 0;

        return _tokenizer.Value.GetIndexByTokenCount(
            text, maxTokens, out string? _, out int _);
    }

    /// <summary>
    /// 取结尾不超过给定 token 数的一段文本。切块重叠靠它——重叠必须按 token 算,
    /// 按字符算会让中英文的实际重叠量差好几倍。
    /// </summary>
    /// <param name="text">原文</param>
    /// <param name="maxTokens">token 上限</param>
    /// <returns>结尾片段与它的 token 数;maxTokens 非正时返回空串</returns>
    public static (string Text, int Tokens) TakeLastTokens(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text) || maxTokens <= 0) return ("", 0);

        // token 数由分词器一并给出,调用方不必再数一遍
        int index = _tokenizer.Value.GetIndexByTokenCountFromEnd(
            text, maxTokens, out string? _, out int tokenCount);
        return index <= 0 ? (text, CountTokens(text)) : (text[index..], tokenCount);
    }

    /// <summary>
    /// 后台加载词表。启动时调一次，此后谁都不必再为"首次使用"付那几十毫秒——
    /// 调用点已经不止输入框一处（工具定义的占用估算也走这里），
    /// 让每个调用方各自记得绕开 UI 线程是迟早会漏的。
    /// </summary>
    public static void Warmup()
    {
        if (_tokenizer.IsValueCreated) return;
        _ = Task.Run(() => _ = _tokenizer.Value);
    }
}
