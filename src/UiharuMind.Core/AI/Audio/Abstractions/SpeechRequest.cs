namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 智能体想念给用户听的一段话（<c>Speak</c> 工具）。用会话角色的声音念，语气可这一句单独给
/// </summary>
/// <param name="Text">要念的话</param>
/// <param name="Instruction">这一句的语气描述；null 用角色卡上的</param>
public sealed record SpeechRequest(string Text, string? Instruction = null);
