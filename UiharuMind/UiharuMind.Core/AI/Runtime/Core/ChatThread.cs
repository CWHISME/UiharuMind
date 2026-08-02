using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Core.Utils.Tools;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.AI.Core;

public static class ChatThread
{
    public static async IAsyncEnumerable<string> InvokePromptStreamingAsync(
        this ModelRunningData? modelRunning, string prompt,
        IReadOnlyDictionary<string, object?>? arguments = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryGetChatClient(modelRunning, out IChatClient? client))
        {
            yield return "Model is not running.";
            yield break;
        }

        string renderedPrompt = CharacterPromptRenderer.Render(prompt, arguments);
        await foreach (string text in StreamChatAsync(client!,
                           [new AIChatMessage(ChatRole.User, renderedPrompt)],
                           null, cancellationToken))
        {
            yield return text;
        }
    }

    private static async IAsyncEnumerable<string> StreamChatAsync(
        IChatClient client, IEnumerable<AIChatMessage> messages, ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // StringBuilder builder = new(64);
        // EmptyDelayUpdater delayUpdater = new();

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                           messages, options, cancellationToken).ConfigureAwait(false))
        {
            string text = update.Text;
            if (string.IsNullOrEmpty(text)) continue;
            // builder.Append(text);
            // ConfigureDelay(delayUpdater, builder.Length);
            // if (delayUpdater.UpdateDelay()) yield return builder.ToString();
            yield return text;
        }

        // yield return builder.ToString();
    }

    private static void ConfigureDelay(EmptyDelayUpdater updater, int length)
    {
        const float maxDelay = 75f;
        float factor = (float)Math.Pow(Math.Min(length / maxDelay, 1f), 3);
        updater.SetDelay((int)(factor * maxDelay) + 50);
    }

    private static bool TryGetChatClient(ModelRunningData? modelRunning, out IChatClient? chatClient)
    {
        chatClient = modelRunning?.ChatClient;
        return modelRunning is { IsRunning: true } && chatClient != null;
    }
}