using System.ComponentModel;
using System.Text.Json.Nodes;
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
        // 手写 AotToolFunction（A2 全量扩展）：显式 schema + handler
        return new AotToolFunction(ToolName,
            "用你自己的声音，通过用户的扬声器把话说出来。" +
            "只对「需要被听见」的句子偶尔使用；说完这句话后返回。",
            AotToolFunction.SchemaFromNode(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["text"] = new JsonObject { ["type"] = "string", ["description"] = "要朗读的内容，纯口语文字（不要 Markdown、代码或 URL）。" },
                    ["tone"] = new JsonObject { ["type"] = "string", ["nullable"] = true, ["default"] = null, ["description"] = "本句可选的语气，如「压低声音、略显紧张」。只有部分声音会遵循；省略则用你平时的声音。" },
                },
                ["required"] = new JsonArray(JsonValue.Create("text")),
            }),
            async (args, ct) =>
            {
                string text = AotToolFunction.Marshaller.String(args, "text")!;
                string? tone = AotToolFunction.Marshaller.String(args, "tone");
                return string.IsNullOrWhiteSpace(text) ? "没有要朗读的内容。"
                    : await speak(new SpeechRequest(text.Trim(), string.IsNullOrWhiteSpace(tone) ? null : tone.Trim())).ConfigureAwait(false)
                        ? "已朗读。"
                        : "未朗读：用户没在看这个会话、停止了它，或播放失败。不要重试；改用文字继续。";
            });
    }
}
