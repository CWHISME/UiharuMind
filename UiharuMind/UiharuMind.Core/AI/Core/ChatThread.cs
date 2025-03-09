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

using System.ClientModel.Primitives;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.Chat;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Core.Utils.Tools;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;
using ILogger = Microsoft.Extensions.Logging.ILogger;

#pragma warning disable SKEXP0110

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

    /// <summary>
    /// 思维链内容获取
    /// </summary>
    static Func<StreamingChatCompletionUpdate?, string?> StreamingReasoningContentAccessor { get; } =
        CreateStreamingReasoningContentAccessor();

    public static async IAsyncEnumerable<string> SendMessageStreamingAsync(this ModelRunningData modelRunning,
        ChatHistory chatHistory,
        [EnumeratorCancellation] CancellationToken token)
    {
        // if (!modelRunning.IsRunning)
        // {
        //     Log.Error("Model is not running.");
        //     yield break;
        // }
        if (SafeCheckModelRunning(ref modelRunning) == false)
        {
            yield return "Model is not running.";
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

    // public static async void SendMessageStreaming(this ModelRunningData modelRunning, ChatHistory chatHistory,
    //     Action<DateTimeOffset>? onMessageStart,
    //     Action<ChatStreamingMessageInfo> onMessageReceived,
    //     Action<ChatStreamingMessageInfo> onMessageStopped,
    //     CancellationToken token)
    // {
    //     if (!modelRunning.IsRunning)
    //     {
    //         Log.Error("Model is not running.");
    //         return;
    //     }
    //
    //     var chat = modelRunning.Kernel.GetRequiredService<IChatCompletionService>();
    //     var builder = StringBuilderPool.Get();
    //     var delayUpdater = SimpleObjectPool<EmptyDelayUpdater>.Get();
    //     delayUpdater.SetDelay(100);
    //
    //     ChatStreamingMessageInfo info = new ChatStreamingMessageInfo();
    //     StreamingChatMessageContent? lastMessage = null;
    //     try
    //     {
    //         await foreach (var content in chat.GetStreamingChatMessageContentsAsync(chatHistory,
    //                            new PromptExecutionSettings(), cancellationToken: token))
    //         {
    //             if (onMessageStart != null)
    //             {
    //                 if (content.Metadata?.TryGetValue("CreatedAt", out object? value) == true &&
    //                     value is DateTimeOffset dateTimeOffset)
    //                     onMessageStart(dateTimeOffset);
    //                 else onMessageStart.Invoke(DateTimeOffset.Now);
    //                 onMessageStart = null;
    //             }
    //
    //             lastMessage = content;
    //             builder.Append(content.Content);
    //             // ReSharper disable once MethodHasAsyncOverload
    //             if (delayUpdater.UpdateDelay())
    //             {
    //                 info.Message = builder.ToString();
    //                 onMessageReceived?.Invoke(info);
    //             }
    //
    //             if (token.IsCancellationRequested) break;
    //         }
    //     }
    //     catch (IOException)
    //     {
    //     }
    //     catch (Exception e)
    //     {
    //         Log.Error("Error in ChatThread.SendMessageStreamingAsync: " + e.Message);
    //     }
    //     finally
    //     {
    //         // info.Message = MarkdownUtils.ToHtml(builder.ToString());
    //         info.Message = (builder.ToString());
    //         if (lastMessage?.InnerContent is StreamingChatCompletionUpdate cp)
    //         {
    //             info.TokenCount = cp.Usage?.TotalTokenCount ?? 0;
    //         }
    //
    //         StringBuilderPool.Release(builder);
    //         SimpleObjectPool<EmptyDelayUpdater>.Release(delayUpdater);
    //         Log.Debug("end of chat thread " + info.TokenCount);
    //         onMessageStopped.Invoke(info);
    //     }
    // }

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
        this ModelRunningData? modelRunning, string prompt,
        KernelArguments? kernelArguments = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // if (!modelRunning.IsRunning)
        // {
        //     yield return "Model is not running.";
        //     yield break;
        // }
        if (SafeCheckModelRunning(ref modelRunning) == false)
        {
            yield return "Model is not running.";
            yield break;
        }

        StringBuilder builder = new(64);
        EmptyDelayUpdater delayUpdater = new();
        await foreach (var content in modelRunning!.Kernel
                           .InvokePromptStreamingAsync(prompt, kernelArguments, cancellationToken: cancellationToken)
                           .ConfigureAwait(false))
        {
            var str = content.ToString();
            if (string.IsNullOrEmpty(str))
            {
                str = StreamingReasoningContentAccessor(content.InnerContent as StreamingChatCompletionUpdate);
                if (string.IsNullOrEmpty(str)) continue;
            }

            builder.Append(str);
            // ReSharper disable once MethodHasAsyncOverload
            if (delayUpdater.UpdateDelay())
            {
                yield return builder.ToString();
            }

            if (cancellationToken.IsCancellationRequested) break;
        }

        yield return builder.ToString();
    }

    // =========================For agent=====================

    public static async IAsyncEnumerable<string> InvokeAgentStreamingAsync(this ModelRunningData? modelRunning,
        ChatSession chatSession, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // if (modelRunning is not { IsRunning: true })
        // {
        //     yield return "Model is not running.";
        //     yield break;
        // }
        //
        // ChatCompletionAgent agent = chatSession.CharacterData.ToAgent(modelRunning.Kernel);
        // StringBuilder builder = new StringBuilder(64);
        // EmptyDelayUpdater delayUpdater = new();
        //
        // await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(
        //                        chatSession.SafeGetHistory(), null, modelRunning.Kernel, cancellationToken)
        //                    .ConfigureAwait(false))
        // {
        //     if (string.IsNullOrEmpty(response.Content))
        //     {
        //         continue;
        //     }
        //
        //     builder.Append(response);
        //     // ReSharper disable once MethodHasAsyncOverload
        //     if (delayUpdater.UpdateDelay())
        //     {
        //         yield return builder.ToString();
        //     }
        //
        //     // var str = response.ToString();
        //     // yield return str;
        //     if (cancellationToken.IsCancellationRequested) break;
        // }
        //
        // yield return builder.ToString();
        // // Log.Debug("end of chat thread " + builder);
        var history = await chatSession.SafeGetHistory();

        var result = InvokeAgentStreamingAsync(modelRunning, chatSession.CharacterData, history,
            chatSession.CustomParams,
            cancellationToken, () =>
            {
                chatSession.History.Add(history[^1]);
                chatSession.AddMessageInfo(DateTime.UtcNow.Ticks);
            });
        await foreach (var message in result)
        {
            yield return message;
        }
    }

    public static IAsyncEnumerable<string> InvokeAgentStreamingAsync(this ModelRunningData? modelRunning,
        CharacterData characterData, ChatHistory? chatHistory = null,
        Dictionary<string, object?>? kernelArguments = null,
        CancellationToken cancellationToken = default, Action? end = null)
    {
        if (SafeCheckModelRunning(ref modelRunning) == false)
        {
            return new AsyncEnumerableWithMessage("Model is not running.");
        }

        return InvokeAgentStreamingAsync(modelRunning, characterData.ToAgent(modelRunning.Kernel, kernelArguments),
            chatHistory,
            cancellationToken, end);
    }

    public static async IAsyncEnumerable<string> InvokeAgentStreamingAsync(this ModelRunningData? modelRunning,
        ChatCompletionAgent agent, ChatHistory? chatHistory = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default, Action? end = null)
    {
        // if (modelRunning is not { IsRunning: true })
        // {
        //     yield return "Model is not running.";
        //     yield break;
        // }
        if (SafeCheckModelRunning(ref modelRunning) == false)
        {
            yield return "Model is not running.";
            yield break;
        }

        // ChatCompletionAgent agent = characterData.ToAgent(modelRunning.Kernel);
        StringBuilder builder = new StringBuilder(64);
        EmptyDelayUpdater delayUpdater = new();
        bool isParseThinking = false;

        chatHistory ??= new ChatHistory();
        // CancellationTokenSource cts = new();
        // cts.Token.Register(() => end?.Invoke());
        cancellationToken.Register(() =>
        {
            if (chatHistory[^1].Role == AuthorRole.User)
            {
                chatHistory.Add(new ChatMessageContent(AuthorRole.Assistant, builder.ToString())
#pragma warning disable SKEXP0001
                    { AuthorName = agent.Name });
#pragma warning restore SKEXP0001
                end?.Invoke();
            }
        });
        await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(
                               chatHistory, null, modelRunning!.Kernel, cancellationToken)
                           .ConfigureAwait(false))
        {
            string? content = response.Content;
            if (string.IsNullOrEmpty(content))
            {
                // var jsonContent = JsonNode.Parse(ModelReaderWriter.Write(update.InnerContent!));
                // var reasoningUpdate = jsonContent!["choices"]![0]!["delta"]!["reasoning_content"];
                content = StreamingReasoningContentAccessor(response.InnerContent as StreamingChatCompletionUpdate);
                if (string.IsNullOrEmpty(content)) continue;
                if (!isParseThinking)
                {
                    builder.Append("<think>");
                    isParseThinking = true;
                }
            }
            else if (isParseThinking)
            {
                builder.Append("</think>");
                isParseThinking = false;
            }

            builder.Append(content);
            const float maxDelay = 75f;
            float factor = (float)Math.Pow(Math.Min(builder.Length / maxDelay, 1f), 3); // 使用三次方加速曲线，前面变化比较缓慢，越靠后越快
            delayUpdater.SetDelay((int)(factor * maxDelay) + 50);
            // ReSharper disable once MethodHasAsyncOverload
            if (delayUpdater.UpdateDelay())
            {
                yield return builder.ToString();
            }

            // var str = response.ToString();
            // yield return str;
            if (cancellationToken.IsCancellationRequested) break;
        }

        var lastMessage = builder.ToString();
        chatHistory[^1].Content = lastMessage;
        yield return lastMessage;
        // Log.Debug("end of chat thread " + builder);
        // chatSession.AddMessageInfo(DateTime.UtcNow.Ticks);
        end?.Invoke();
    }

    /// <summary>
    /// 以 userInput 作为输入，要求 agent 讨论出一个结果
    /// </summary>
    /// <param name="modelRunning"></param>
    /// <param name="userInput"></param>
    /// <param name="terminationStrategy"></param>
    /// <param name="cancellationToken"></param>
    /// <param name="agents"></param>
    /// <returns></returns>
    public static async IAsyncEnumerable<string> InvokeAutoChatGroupPlanningStreamingAsync(
        this ModelRunningData? modelRunning, string userInput,
        TerminationStrategy terminationStrategy,
        [EnumeratorCancellation] CancellationToken cancellationToken = default, params Agent[] agents)
    {
        // if (modelRunning is not { IsRunning: true })
        // {
        //     if (modelRunning?.IsRemoteModel == false)
        //     {
        //         yield return "Model is not running.";
        //         yield break;
        //     }
        //
        //     if (modelRunning?.Kernel == null)
        //     {
        //         _ = modelRunning?.StartLoad(null, null);
        //     }
        // }

        if (SafeCheckModelRunning(ref modelRunning) == false)
        {
            yield return "Model is not running.";
            yield break;
        }

        AgentGroupChat chat = new(agents)
        {
            ExecutionSettings =
                new()
                {
                    TerminationStrategy = terminationStrategy
                }
        };

        ChatMessageContent input = new(AuthorRole.User, userInput);
        chat.AddChatMessage(input);

        StringBuilder builder = new StringBuilder(64);
        EmptyDelayUpdater delayUpdater = new();
        delayUpdater.SetDelay(100);
        await foreach (StreamingChatMessageContent response in chat.InvokeStreamingAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(response.Content))
            {
                continue;
            }

            builder.Append(response);
            // ReSharper disable once MethodHasAsyncOverload
            if (delayUpdater.UpdateDelay())
            {
                yield return builder.ToString();
            }

            // yield return response.ToString();
            if (cancellationToken.IsCancellationRequested) break;
        }
        // Log.Debug(chat);
    }

    private static bool SafeCheckModelRunning(ref ModelRunningData? modelRunning)
    {
        if (modelRunning is not { IsRunning: true })
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 由于 OpenAI 的 API 限制，无法直接获取到 reasoning_content，因此需要使用反射来获取。
    /// 创建一个从 StreamingChatCompletionUpdate 对象中提取 reasoning_content 的委托。
    /// 如果未找到或无法解析，则返回 null。
    /// </summary>
    public static Func<StreamingChatCompletionUpdate?, string?> CreateStreamingReasoningContentAccessor()
    {
        // 1. 获取 StreamingChatCompletionUpdate 类型
        Type streamingChatType = typeof(StreamingChatCompletionUpdate);

        // 2. 获取 internal 属性 "Choices"
        //    类型：IReadOnlyList<InternalCreateChatCompletionStreamResponseChoice>
        PropertyInfo? choicesProp = streamingChatType.GetProperty(
            "Choices",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        ) ?? throw new InvalidOperationException(
            "Unable to reflect property 'Choices' in StreamingChatCompletionUpdate.");

        // 3. 获取 Choices 的泛型参数 T = InternalCreateChatCompletionStreamResponseChoice
        Type? choicesPropType = choicesProp.PropertyType ??
                                throw new InvalidOperationException(
                                    "Unable to determine the property type of 'Choices'."); // IReadOnlyList<T>

        if (!choicesPropType.IsGenericType || choicesPropType.GetGenericArguments().Length != 1)
        {
            throw new InvalidOperationException(
                "Property 'Choices' is not the expected generic type IReadOnlyList<T>.");
        }

        // 取得 T
        Type choiceType = choicesPropType.GetGenericArguments()[0];

        // 4. 从 choiceType 中获取 internal 属性 "Delta"
        PropertyInfo? deltaProp = choiceType.GetProperty(
            "Delta",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        ) ?? throw new InvalidOperationException("Unable to reflect property 'Delta' in choice type.");

        // 5. 获取 Delta 对象的类型，然后从中获取 "SerializedAdditionalRawData"
        Type deltaType = deltaProp.PropertyType;
        PropertyInfo? rawDataProp = deltaType.GetProperty(
            "SerializedAdditionalRawData",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        ) ?? throw new InvalidOperationException(
            "Unable to reflect property 'SerializedAdditionalRawData' in delta type.");

        // ---
        // 创建并返回委托，在委托中使用上述缓存的 PropertyInfo
        // ---
        return streamingChatObj =>
        {
            if (streamingChatObj == null)
            {
                return null;
            }

            // 拿到 choices 数据
            object? choicesObj = choicesProp.GetValue(streamingChatObj);
            if (choicesObj is not IEnumerable choicesEnumerable)
            {
                return null;
            }

            foreach (object? choice in choicesEnumerable)
            {
                if (choice == null) continue;

                // 获取 Delta 对象
                object? deltaObj = deltaProp.GetValue(choice);
                if (deltaObj == null) continue;

                // 获取字典 SerializedAdditionalRawData
                object? rawDataValue = rawDataProp.GetValue(deltaObj);
                if (rawDataValue is not IDictionary<string, BinaryData> dict) continue;
                // 从字典里查找 "reasoning_content"
                if (dict.TryGetValue("reasoning_content", out BinaryData? binaryData))
                {
                    return binaryData.ToObjectFromJson<string>();
                }
            }

            // 如果所有 Choice 中都没有找到则返回 null
            return null;
        };
    }
}