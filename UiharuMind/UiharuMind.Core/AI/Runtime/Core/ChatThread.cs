using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
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

    /// <summary>
    /// 主聊天直接使用 IChatClient，由项目会话负责上下文、记忆和持久化。
    /// </summary>
    public static async IAsyncEnumerable<string> InvokeAgentStreamingAsync(this ModelRunningData? modelRunning,
        ChatSession chatSession, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryGetChatClient(modelRunning, out IChatClient? client))
        {
            yield return "Model is not running.";
            yield break;
        }

        List<AIChatMessage> messages = await chatSession.BuildRequestMessagesAsync();
        string instructions = CharacterPromptRenderer.Render(
            chatSession.CharacterData.Template, chatSession.CharacterData.BuildPromptArguments(chatSession.CustomParams));
        if (!string.IsNullOrWhiteSpace(instructions))
            messages.Insert(0, new AIChatMessage(ChatRole.System, instructions));

        string finalText = "";
        try
        {
            await foreach (string text in StreamChatAsync(client!, messages,
                               chatSession.CharacterData.Config.ExecutionSettings.ToChatOptions(), cancellationToken))
            {
                finalText = text;
                yield return text;
            }
        }
        finally
        {
            // 取消生成时也保存已经收到的有效内容，且只在此处写入一次。
            chatSession.AddGeneratedAssistantMessage(finalText);
        }
    }

    public static IAsyncEnumerable<string> InvokeAgentStreamingAsync(this ModelRunningData? modelRunning,
        CharacterData characterData, IEnumerable<ChatMessageData>? history = null,
        Dictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetChatClient(modelRunning, out IChatClient? client))
            return new AsyncEnumerableWithMessage("Model is not running.");

        ChatClientAgent agent = characterData.ToAgent(client!, arguments);
        List<AIChatMessage> messages = history?.Select(x => x.ToAIMessage()).ToList() ?? [];
        return InvokeAgentStreamingAsync(agent, messages, cancellationToken);
    }

    public static async IAsyncEnumerable<string> InvokeAgentStreamingAsync(
        ChatClientAgent agent, IEnumerable<AIChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StringBuilder builder = new(64);
        EmptyDelayUpdater delayUpdater = new();

        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(
                           messages, null, null, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(update.Text)) continue;
            builder.Append(update.Text);
            ConfigureDelay(delayUpdater, builder.Length);
            if (delayUpdater.UpdateDelay()) yield return builder.ToString();
        }

        yield return builder.ToString();
    }

    public static async IAsyncEnumerable<string> InvokeSequentialAgentWorkflowStreamingAsync(
        this ModelRunningData? modelRunning, string input,
        CharacterData firstCharacter, CharacterData secondCharacter,
        Dictionary<string, object?>? arguments = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryGetChatClient(modelRunning, out IChatClient? client))
        {
            yield return "Model is not running.";
            yield break;
        }

        ChatClientAgent first = firstCharacter.ToAgent(client!, arguments);
        ChatClientAgent second = secondCharacter.ToAgent(client!, arguments);

        // 顺序 Workflow 会把翻译初稿交给审核 Agent，UI 只接收最终节点的输出。
        Workflow workflow = AgentWorkflowBuilder.BuildSequential([first, second]);
        AIAgent workflowAgent = workflow.AsAIAgent(
            name: "TranslationReviewWorkflow",
            description: "Translate and review text in two deterministic steps.");

        StringBuilder builder = new(64);
        await foreach (AgentResponseUpdate update in workflowAgent.RunStreamingAsync(
                           input, null, null, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(update.Text)) continue;
            builder.Append(update.Text);
            yield return builder.ToString();
        }

        Log.Debug("Translation review workflow completed.");
    }

    private static async IAsyncEnumerable<string> StreamChatAsync(
        IChatClient client, IEnumerable<AIChatMessage> messages, ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StringBuilder builder = new(64);
        EmptyDelayUpdater delayUpdater = new();

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                           messages, options, cancellationToken).ConfigureAwait(false))
        {
            string? text = update.Text;
            if (string.IsNullOrEmpty(text)) continue;
            builder.Append(text);
            ConfigureDelay(delayUpdater, builder.Length);
            if (delayUpdater.UpdateDelay()) yield return builder.ToString();
        }

        yield return builder.ToString();
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
