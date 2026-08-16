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
