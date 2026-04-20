using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.Skills;

public abstract class AgentSkillVisionBase : AgentSkillConvertableBase
{
    protected readonly byte[] _imageBytes;

    public AgentSkillVisionBase(byte[] imageBytes)
    {
        _imageBytes = imageBytes;
    }
    
    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        if (!modelRunningData.IsVisionModel)
        {
            // var visionModel = LlmManager.Instance.RemoteModelManager.FindVisionModel();
            // if (visionModel == null)
            return new AsyncEnumerableWithMessage("Not support vision model.");
            // modelRunningData = visionModel;
        }

        var ocr = GetCharacterData().ToAgent(modelRunningData!.Kernel!, args);
        _chatHistory = new ChatHistory();
        _chatHistory.AddUserMessage([
            // new ImageContent(new Uri(""))
            new ImageContent(_imageBytes, "image/jpg"),
            // new TextContent(text),
        ]);
        // return modelRunningData.SendMessageStreamingAsync(chatHistory, cancellationToken);
        return modelRunningData.InvokeAgentStreamingAsync(ocr, _chatHistory, cancellationToken);
    }

    protected override bool IsVision => true;
}