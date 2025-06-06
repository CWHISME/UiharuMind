using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.Skills;

/// <summary>
/// ocr agent skill
/// </summary>
public class ImageOcrSkill : AgentSkillVisionBase
{
    // private readonly byte[] _imageBytes;
    //
    // public ImageOcrSkill(byte[] imageBytes)
    // {
    //     _imageBytes = imageBytes;
    // }

    public ImageOcrSkill(byte[] imageBytes) : base(imageBytes)
    {
    }

    protected override IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string text,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default)
    {
        if (false == modelRunningData?.IsVisionModel)
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

    protected override CharacterData GetCharacterData()
    {
        return DefaultCharacterManager.Instance.GetCharacterData(DefaultCharacter.VisionOcr);
    }
}