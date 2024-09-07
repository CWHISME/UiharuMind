using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using UiharuMind.Core.Core.LLM;

namespace UiharuMind.Core.Core.Chat;

public class ChatThread
{
    public string Name { get; set; }
    public List<ChatMessage> Messages { get; set; }

    private Kernel? _kernel;
    private ChatHistory _chatHistory;

    private Kernel GetKernel
    {
        get
        {
            if (_kernel == null)
            {
                _kernel = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion("m", "k", httpClient: new HttpClient(new SKernelHttpDelegatingHandler()))
                    .Build();
            }

            return _kernel;
        }
    }

    public ChatThread()
    {
        Messages = new List<ChatMessage>();
    }

    public async Task<string> SendUserMessageStreamingAsync(string message, Action<string?> onMessageReceived)
    {
        _chatHistory.AddMessage(AuthorRole.User, message);
        //
        // var args = new KernelArguments();
        // args.Add();
        // var response = await GetKernel.InvokePromptAsync("promptTemplate", _chatHistory);
        // IChatCompletionService
        var chat = GetKernel.GetRequiredService<IChatCompletionService>();

        StreamingChatMessageContent? result = null;
        await foreach (var content in chat.GetStreamingChatMessageContentsAsync(_chatHistory,
                           GetOpenAIRequestSettings()))
        {
            result = content;
            onMessageReceived.Invoke(result.Content);
        }

        string str = result?.Content ?? "";
        _chatHistory.AddMessage(AuthorRole.Assistant, str);

        return str;
    }

    private OpenAIPromptExecutionSettings GetOpenAIRequestSettings()
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