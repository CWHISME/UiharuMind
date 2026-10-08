using System.ComponentModel;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 朗读工具：智能体把一段话用自己的声音念给用户听，语气可每次自己给。
/// 只在界面正看着这个会话时出声，念完才返回——没出声（没在看、被停、播放失败）如实告诉模型
/// </summary>
public static class SpeakTool
{
    /// <summary>工具名</summary>
    public const string ToolName = "Speak";

    /// <summary>
    /// 创建朗读 AIFunction
    /// </summary>
    /// <param name="speak">交给界面去念：请求 → 念到结尾为 true</param>
    /// <returns>工具实例</returns>
    public static AITool Create(Func<SpeechRequest, Task<bool>> speak)
    {
        return AIFunctionFactory.Create(
            async ([Description("What to say, as plain spoken words (no Markdown, code or URLs).")]
                    string text,
                    [Description("Optional tone for this line, e.g. \"whispering, a little nervous\". " +
                                 "Only some voices follow it; omit to use your usual voice.")]
                    string? tone = null) =>
                string.IsNullOrWhiteSpace(text) ? "Nothing to say."
                : await speak(new SpeechRequest(text.Trim(), string.IsNullOrWhiteSpace(tone) ? null : tone.Trim()))
                    .ConfigureAwait(false)
                    ? "Spoken aloud."
                    : "Not spoken: the user is not viewing this conversation, stopped it, or playback failed. " +
                      "Do not retry; carry on in text.",
            ToolName,
            "Say something aloud to the user through their speakers, in your own voice. " +
            "Use it sparingly for lines that are meant to be heard; it returns after the line has been spoken.");
    }
}
