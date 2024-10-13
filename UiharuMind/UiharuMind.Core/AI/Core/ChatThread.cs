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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Chat;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Core.Utils.Tools;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace UiharuMind.Core.AI.Core;

public static class ChatThread
{
    // public string Name { get; set; }
    // public List<ChatMessage> Messages { get; set; }

    // private Kernel _kernel;
    //
    // // private ChatHistory _chatHistory;
    //
    // private Kernel GetKernel
    // {
    //     get
    //     {
    //         if (_kernel == null)
    //         {
    //             _kernel = Kernel.CreateBuilder()
    //                 .AddOpenAIChatCompletion("UiharuMind", "Empty",
    //                     httpClient: new HttpClient(new SKernelHttpDelegatingHandler()))
    //                 .Build();
    //         }
    //
    //         return _kernel;
    //     }
    // }

    // public ChatThread(Kernel kernal)
    // {
    //     _kernel = kernal;
    // }

    public static async IAsyncEnumerable<string> SendMessageStreamingAsync(this ModelRunningData modelRunning,
        ChatHistory chatHistory,
        [EnumeratorCancellation] CancellationToken token)
    {
        if (!modelRunning.IsRunning)
        {
            Log.Error("Model is not running.");
            yield break;
        }

        var chat = modelRunning.Kernel.GetRequiredService<IChatCompletionService>();
        var builder = StringBuilderPool.Get();
        var delayUpdater = SimpleObjectPool<EmptyDelayUpdater>.Get();
        delayUpdater.SetDelay(100);
        // Stopwatch stopwatch = Stopwatch.StartNew();
        await foreach (var content in chat.GetStreamingChatMessageContentsAsync(chatHistory,
                           new PromptExecutionSettings(), cancellationToken: token).ConfigureAwait(false))
        {
            builder.Append(content.Content);
            // var startTimestamp = (DateTimeOffset)content.Metadata["CreatedAt"];
            // ReSharper disable once MethodHasAsyncOverload
            if (delayUpdater.UpdateDelay())
            {
                // yield return MarkdownUtils.ToHtml(builder.ToString());
                // Log.Debug(stopwatch.ElapsedMilliseconds);
                // stopwatch.Restart();
                yield return builder.ToString();
            }

            // Log.Debug($"Received message: {content.Content}");
            if (token.IsCancellationRequested) break;
        }

        yield return builder.ToString();
        // Log.Debug("end of chat thread " + stopwatch.ElapsedMilliseconds);
        // yield return MarkdownUtils.ToHtml(builder.ToString());

        StringBuilderPool.Release(builder);
        SimpleObjectPool<EmptyDelayUpdater>.Release(delayUpdater);
    }

    public static async void SendMessageStreaming(this ModelRunningData modelRunning, ChatHistory chatHistory,
        Action<DateTimeOffset>? onMessageStart,
        Action<ChatStreamingMessageInfo> onMessageReceived,
        Action<ChatStreamingMessageInfo> onMessageStopped,
        CancellationToken token)
    {
        if (!modelRunning.IsRunning)
        {
            Log.Error("Model is not running.");
            return;
        }

        var chat = modelRunning.Kernel.GetRequiredService<IChatCompletionService>();
        var builder = StringBuilderPool.Get();
        var delayUpdater = SimpleObjectPool<EmptyDelayUpdater>.Get();
        delayUpdater.SetDelay(100);

        ChatStreamingMessageInfo info = new ChatStreamingMessageInfo();
        StreamingChatMessageContent? lastMessage = null;
        try
        {
            await foreach (var content in chat.GetStreamingChatMessageContentsAsync(chatHistory,
                               new PromptExecutionSettings(), cancellationToken: token))
            {
                if (onMessageStart != null)
                {
                    if (content.Metadata?.TryGetValue("CreatedAt", out object? value) == true &&
                        value is DateTimeOffset dateTimeOffset)
                        onMessageStart(dateTimeOffset);
                    else onMessageStart.Invoke(DateTimeOffset.Now);
                    onMessageStart = null;
                }

                lastMessage = content;
                builder.Append(content.Content);
                // ReSharper disable once MethodHasAsyncOverload
                if (delayUpdater.UpdateDelay())
                {
                    info.Message = builder.ToString();
                    onMessageReceived?.Invoke(info);
                }

                if (token.IsCancellationRequested) break;
            }
        }
        catch (IOException)
        {
        }
        catch (Exception e)
        {
            Log.Error("Error in ChatThread.SendMessageStreamingAsync: " + e.Message);
        }
        finally
        {
            // info.Message = MarkdownUtils.ToHtml(builder.ToString());
            info.Message = (builder.ToString());
            if (lastMessage?.InnerContent is StreamingChatCompletionUpdate cp)
            {
                info.TokenCount = cp.Usage?.TotalTokenCount ?? 0;
            }

            StringBuilderPool.Release(builder);
            SimpleObjectPool<EmptyDelayUpdater>.Release(delayUpdater);
            Log.Debug("end of chat thread " + info.TokenCount);
            onMessageStopped.Invoke(info);
        }
    }

    /// <summary>
    /// 使用提示词，直接生成流式消息
    /// content 为需要询问的内容，prompt 为提示词，culture 为语言
    /// 如果 message 为 null，则直接使用 content 作为 message
    /// </summary>
    /// <param name="modelRunning"></param>
    /// <param name="content"></param>
    /// <param name="prompt"></param>
    /// <param name="culture"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public static IAsyncEnumerable<string> InvokeQuickToolPromptStreamingAsync(
        this ModelRunningData modelRunning, string content, string? prompt = null, CultureInfo? culture = null,
        CancellationToken token = default)
    {
        KernelArguments? kernelArgs = prompt == null
            ? null
            : new KernelArguments()
            {
                { "lang", culture?.Name ?? CultureInfo.CurrentCulture.Name },
                { "content", content }
            };

        // return (var message in LlmManager.Instance.CurrentRunningModel.InvokePromptStreamingAsync(
        //                    prompt ?? content, kernelArgs))
        return modelRunning.InvokePromptStreamingAsync(prompt ?? content, kernelArgs, token);
    }

    /// <summary>
    /// 使用提示词，直接生成流式消息
    /// </summary>
    /// <param name="modelRunning"></param>
    /// <param name="prompt"></param>
    /// <param name="kernelArguments"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public static async IAsyncEnumerable<string> InvokePromptStreamingAsync(
        this ModelRunningData modelRunning, string prompt,
        KernelArguments? kernelArguments = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!modelRunning.IsRunning)
        {
            yield return "Model is not running.";
            yield break;
        }

        // StringBuilder builder = new();
        await foreach (var content in modelRunning.Kernel
                           .InvokePromptStreamingAsync(prompt, kernelArguments, cancellationToken: cancellationToken)
                           .ConfigureAwait(false))
        {
            // builder.Append(content);
            yield return content.ToString();
            if (cancellationToken.IsCancellationRequested) break;
        }
    }

    // =========================For agent=====================

    [Experimental("SKEXP0110")]
    public static async IAsyncEnumerable<string> InvokeAgentStreamingAsync(this ModelRunningData modelRunning,
        ChatSession chatSession, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!modelRunning.IsRunning)
        {
            yield return "Model is not running.";
            yield break;
        }

        await foreach (StreamingChatMessageContent response in chatSession.CharacterData.ChatAgent.InvokeStreamingAsync(
                           chatSession.History, null, modelRunning.Kernel, cancellationToken))
        {
            if (string.IsNullOrEmpty(response.Content))
            {
                continue;
            }

            yield return response.ToString();
            if (cancellationToken.IsCancellationRequested) break;
        }
    }
}