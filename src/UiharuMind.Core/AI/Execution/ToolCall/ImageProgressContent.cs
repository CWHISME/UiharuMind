using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.ImageGeneration;

namespace UiharuMind.Core.AI.Execution.ToolCall;

/// <summary>
/// 生图工具跑到哪一步了：本地出一张图要几分钟，工具卡上据此显示阶段与步数。
/// 不进历史、不回喂模型——与 <see cref="SubSessionStartedContent"/> 同一性质
/// </summary>
public sealed class ImageProgressContent : AIContent
{
    /// <summary>那次工具调用的标识（界面据此找到对应卡片）</summary>
    public string CallId { get; }

    /// <summary>进度（不带预览图：卡片不画中间图，也不必在通道里攥着它）</summary>
    public ImageProgress Progress { get; }

    /// <param name="callId">工具调用标识</param>
    /// <param name="progress">进度</param>
    public ImageProgressContent(string callId, ImageProgress progress)
    {
        CallId = callId;
        Progress = progress;
    }
}
