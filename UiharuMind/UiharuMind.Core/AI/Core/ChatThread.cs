using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
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
        Stopwatch stopwatch = Stopwatch.StartNew();
        await foreach (var content in chat.GetStreamingChatMessageContentsAsync(chatHistory,
                           GetOpenAiRequestSettings(), cancellationToken: token).ConfigureAwait(false))
        {
            builder.Append(content.Content);

            // ReSharper disable once MethodHasAsyncOverload
            if (delayUpdater.UpdateDelay())
            {
                // yield return MarkdownUtils.ToHtml(builder.ToString());
                Log.Debug(stopwatch.ElapsedMilliseconds);
                stopwatch.Restart();
                yield return builder.ToString();
            }

            // Log.Debug($"Received message: {content.Content}");
            if (token.IsCancellationRequested) break;
        }

        yield return builder.ToString();
        Log.Debug("end of chat thread " + stopwatch.ElapsedMilliseconds);
        // yield return MarkdownUtils.ToHtml(builder.ToString());

        StringBuilderPool.Release(builder);
        SimpleObjectPool<EmptyDelayUpdater>.Release(delayUpdater);
    }


    public async void SendMessageStreaming(ChatHistory chatHistory,
        Action<string> onMessageReceived, Action<string> onMessageStopped, CancellationToken token)
    {
        var chat = GetKernel.GetRequiredService<IChatCompletionService>();
        var builder = StringBuilderPool.Get();
        string result = "";

        try
        {
            await foreach (var content in chat.GetStreamingChatMessageContentsAsync(chatHistory,
                               GetOpenAiRequestSettings(), cancellationToken: token).ConfigureAwait(false))
            {
                builder.Append(content.Content);
                result = builder.ToString();
                onMessageReceived?.Invoke(result);
            }
        }
        catch (IOException)
        {
        }
        catch (Exception e)
        {
            Log.Error("Error in ChatThread.SendMessageStreamingAsync: " + e.Message);
        }

        StringBuilderPool.Release(builder);
        onMessageStopped?.Invoke(result);
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