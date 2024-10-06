using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Core.Utils.Tools;

namespace UiharuMind.Core.AI.Core;

public class ChatThread
{
    // public string Name { get; set; }
    // public List<ChatMessage> Messages { get; set; }

    private Kernel? _kernel;

    // private ChatHistory _chatHistory;

    private Kernel GetKernel
    {
        get
        {
            if (_kernel == null)
            {
                _kernel = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion("UiharuMind", "Empty",
                        httpClient: new HttpClient(new SKernelHttpDelegatingHandler()))
                    .Build();
            }

            return _kernel;
        }
    }

    public async IAsyncEnumerable<string> SendMessageStreamingAsync(ChatHistory chatHistory,
        [EnumeratorCancellation] CancellationToken token)
    {
        var chat = GetKernel.GetRequiredService<IChatCompletionService>();
        var builder = StringBuilderPool.Get();
        var delayUpdater = SimpleObjectPool<EmptyDelayUpdater>.Get();
        delayUpdater.SetDelay(100);
        // Stopwatch stopwatch = Stopwatch.StartNew();
        await foreach (var content in chat.GetStreamingChatMessageContentsAsync(chatHistory,
                           GetOpenAiRequestSettings(), cancellationToken: token).ConfigureAwait(false))
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

    public async void SendMessageStreaming(ChatHistory chatHistory,
        Action<DateTimeOffset>? onMessageStart,
        Action<ChatStreamingMessageInfo> onMessageReceived,
        Action<ChatStreamingMessageInfo> onMessageStopped,
        CancellationToken token)
    {
        var chat = GetKernel.GetRequiredService<IChatCompletionService>();
        var builder = StringBuilderPool.Get();
        var delayUpdater = SimpleObjectPool<EmptyDelayUpdater>.Get();
        delayUpdater.SetDelay(100);

        ChatStreamingMessageInfo info = new ChatStreamingMessageInfo();
        StreamingChatMessageContent? lastMessage = null;
        try
        {
            await foreach (var content in chat.GetStreamingChatMessageContentsAsync(chatHistory,
                               GetOpenAiRequestSettings(), cancellationToken: token))
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
                info.TokenCount = cp.Usage?.TotalTokens ?? 0;
            }

            StringBuilderPool.Release(builder);
            SimpleObjectPool<EmptyDelayUpdater>.Release(delayUpdater);
            Log.Debug("end of chat thread " + info.TokenCount);
            onMessageStopped.Invoke(info);
        }
    }

    private OpenAIPromptExecutionSettings GetOpenAiRequestSettings()
    {
        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 1,
            MaxTokens = 2048,
            TopP = 1,
            FrequencyPenalty = 0,
            PresencePenalty = 0,
        };

        // settings.ChatSystemPrompt = "";

        return settings;
    }
}