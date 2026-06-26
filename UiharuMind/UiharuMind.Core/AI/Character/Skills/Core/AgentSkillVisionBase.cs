using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Chat;

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

        _chatHistory =
        [
            new ChatMessageData
            {
                Role = ECharacter.User,
                Content = text,
                ImageBytes = _imageBytes,
                ImageMediaType = "image/jpeg"
            }
        ];
        return modelRunningData.InvokeAgentStreamingAsync(GetCharacterData(), _chatHistory, args, cancellationToken);
    }

    protected override bool IsVision => true;
}
