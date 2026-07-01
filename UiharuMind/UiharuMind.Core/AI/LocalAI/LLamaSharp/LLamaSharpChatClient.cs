/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 ****************************************************************************/

using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.LocalAI.LLamaSharp;

internal sealed class LLamaSharpChatClient(
    ILLamaExecutor executor,
    IHistoryTransform historyTransform,
    LLamaWeights weights,
    int contextSize) : IChatClient
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string text = "";
        await foreach (ChatResponseUpdate update in GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            text += update.Text;
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InferenceParams inferenceParams = CreateInferenceParams(options);
            string prompt = BuildPromptWithinContext(messages, options, inferenceParams);

            await foreach (string text in executor.InferAsync(prompt, inferenceParams, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(text))
                    yield return new ChatResponseUpdate(ChatRole.Assistant, text);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        if (executor is IDisposable disposable) disposable.Dispose();
        _semaphore.Dispose();
    }

    private static InferenceParams CreateInferenceParams(ChatOptions? options)
    {
        DefaultSamplingPipeline samplingPipeline = new()
        {
            Temperature = options?.Temperature is { } temperature ? (float)temperature : 0.8f,
            TopP = options?.TopP is { } topP ? (float)topP : 0.95f,
            TopK = options?.TopK ?? 40,
            FrequencyPenalty = options?.FrequencyPenalty is { } frequencyPenalty ? (float)frequencyPenalty : 0,
            PresencePenalty = options?.PresencePenalty is { } presencePenalty ? (float)presencePenalty : 0,
            Seed = options?.Seed is { } seed ? (uint)seed : 0
        };

        return new InferenceParams
        {
            MaxTokens = options?.MaxOutputTokens ?? -1,
            AntiPrompts = CreateAntiPrompts(options),
            // LLamaSharp 默认在上下文满时抛异常；产品聊天更需要自动裁剪旧上下文继续运行。
            OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill,
            ContextTruncationPercentage = 0.1f,
            SamplingPipeline = samplingPipeline
        };
    }

    private string BuildPromptWithinContext(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        InferenceParams inferenceParams)
    {
        List<ChatMessage> candidates = messages
            .Where(static message => !string.IsNullOrWhiteSpace(message.Text))
            .ToList();
        int reservedOutputTokens = GetReservedOutputTokens(options);
        int maxPromptTokens = Math.Max(64, contextSize - reservedOutputTokens);

        while (true)
        {
            ChatHistory history = ToChatHistory(candidates, options);
            string prompt = historyTransform.HistoryToText(history);
            if (GetTokenCount(prompt) <= maxPromptTokens)
                return prompt;

            int removeIndex = FindOldestRemovableMessageIndex(candidates);
            if (removeIndex < 0)
            {
                if (!TryTrimLastUserMessage(candidates))
                    throw new InvalidOperationException("The prompt is too large for the current local model context window.");
                continue;
            }

            // 初始 prompt 过长时，LLamaSharp 在首轮 DecodeAsync 前不会触发 overflow 策略，只能先裁掉最旧上下文。
            candidates.RemoveAt(removeIndex);
        }
    }

    private int GetTokenCount(string prompt)
    {
        try
        {
            return weights.Tokenize(prompt, true, true, Encoding.UTF8).Length;
        }
        catch
        {
            return Math.Max(1, prompt.Length / 3);
        }
    }

    private int GetReservedOutputTokens(ChatOptions? options)
    {
        int requested = options?.MaxOutputTokens is > 0 ? options.MaxOutputTokens.Value : Math.Min(512, contextSize / 4);
        return Math.Clamp(requested, 32, Math.Max(32, contextSize / 2));
    }

    private static int FindOldestRemovableMessageIndex(IReadOnlyList<ChatMessage> messages)
    {
        for (int i = 0; i < messages.Count - 1; i++)
        {
            if (messages[i].Role != ChatRole.System)
                return i;
        }

        return -1;
    }

    private static bool TryTrimLastUserMessage(IList<ChatMessage> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            ChatMessage message = messages[i];
            if (message.Role == ChatRole.System || string.IsNullOrWhiteSpace(message.Text))
                continue;

            string text = message.Text;
            if (text.Length < 128)
                return false;

            messages[i] = new ChatMessage(message.Role, text[..Math.Max(64, text.Length / 2)]);
            return true;
        }

        return false;
    }

    private static List<string> CreateAntiPrompts(ChatOptions? options)
    {
        List<string> antiPrompts = options?.StopSequences?.ToList() ?? [];
        if (!antiPrompts.Contains("User:", StringComparer.OrdinalIgnoreCase))
            antiPrompts.Add("User:");
        return antiPrompts;
    }

    private static ChatHistory ToChatHistory(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        ChatHistory history = new();
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
            history.AddMessage(AuthorRole.System, options.Instructions);

        foreach (ChatMessage message in messages)
        {
            string text = message.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            history.AddMessage(ToAuthorRole(message.Role), text);
        }

        return history;
    }

    private static AuthorRole ToAuthorRole(ChatRole role)
    {
        if (role == ChatRole.System) return AuthorRole.System;
        if (role == ChatRole.Assistant) return AuthorRole.Assistant;
        if (role == ChatRole.Tool) return AuthorRole.Unknown;
        return AuthorRole.User;
    }
}
