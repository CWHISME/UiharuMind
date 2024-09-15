using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Chat;

public class ChatThread
{
    // public string Name { get; set; }
    // public List<ChatMessage> Messages { get; set; }

    private Kernel? _kernel;

    // private ChatHistory _chatHistory;
    private StringBuilder _resultStringBuilder = new StringBuilder();

    private Kernel GetKernel
    {
        get
        {
            if (_kernel == null)
            {
                _kernel = Kernel.CreateBuilder()
                    .AddOpenAIChatCompletion("UiharuMind", "Empty", httpClient: new HttpClient(new SKernelHttpDelegatingHandler()))
                    .Build();
            }

            return _kernel;
        }
    }

    // public ChatThread()
    // {
    //     Messages = new List<ChatMessage>();
    // }

    public async Task<string> SendMessageStreamingAsync(ChatHistory chatHistory, Action<string> onMessageReceived)
    {
        // _chatHistory.AddMessage(AuthorRole.User, message);
        //
        // var args = new KernelArguments();
        // args.Add();
        // var response = await GetKernel.InvokePromptAsync("promptTemplate", _chatHistory);
        // IChatCompletionService
        var chat = GetKernel.GetRequiredService<IChatCompletionService>();
        string result = "";
        try
        {
            await foreach (var content in chat.GetStreamingChatMessageContentsAsync(chatHistory,
                               GetOpenAiRequestSettings()))
            {
                _resultStringBuilder.Append(content.Content);
                result = _resultStringBuilder.ToString();
                onMessageReceived?.Invoke(result);
                //onMessageReceived?.Invoke(_resultStringBuilder.ToString());
            }
        }
        catch (Exception e)
        {
            Log.Error("Error in ChatThread.SendMessageStreamingAsync: "+e.Message);
        }

        // return _resultStringBuilder.ToString();
        //chatHistory.AddMessage(AuthorRole.Assistant, result);

        // return str;
        return result;
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