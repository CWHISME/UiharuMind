using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.AI.Character.Skills;

public abstract class AgentSkillVisionBase : AgentSkillBase
{
    protected readonly byte[] _imageBytes;

    public AgentSkillVisionBase(byte[] imageBytes)
    {
        _imageBytes = imageBytes;
    }

    protected override bool IsVision => true;
}