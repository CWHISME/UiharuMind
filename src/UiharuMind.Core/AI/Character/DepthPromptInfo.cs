using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Character;

/// <summary>
/// 角色卡 extensions.depth_prompt 的应用侧形态：把一段提示插到历史倒数第 Depth 条的位置。
/// 只作为静态数据随卡走；实际插入由
/// <see cref="UiharuMind.Core.AI.Execution.History.DepthPromptHistoryProvider"/> 做。
/// </summary>
public sealed class DepthPromptInfo
{
    /// <summary>要插入的提示正文。</summary>
    public string Prompt { get; set; } = "";

    /// <summary>距历史末尾的深度：0 = 插在最后，1 = 最后一条之前，以此类推。</summary>
    public int Depth { get; set; }

    /// <summary>插入消息的角色（"system" / "user" / "assistant"）。</summary>
    public string Role { get; set; } = "";

    public ChatRole ToChatRole() => Role switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.System,
    };
}