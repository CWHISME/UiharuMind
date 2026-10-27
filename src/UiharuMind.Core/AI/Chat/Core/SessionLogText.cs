using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 会话流水的 markdown 文本：把 <see cref="ChatSession.History"/> 导成人可读、模型可读的一份文件。
/// 群流水走 <see cref="Group.GroupLogText"/>（发言人标注、化身隔离那套），这里管普通会话
/// （子代理/主会话）的历史导出——消息带思考与<b>工具调用原样参数</b>（参数类型传错这类问题只有在这份导出的原样参数里看得见）。
/// chatsession.dump 的会话分支用，格式与 App 层开发导出 session.dump 同口径。
/// </summary>
public static class SessionLogText
{
    /// <summary>会话流水文件固定名前缀（草稿目录里，后缀是会话短号——多个会话共用一间草稿目录，同名标题会互相覆盖）</summary>
    public const string FileNamePrefix = "会话流水";

    /// <summary>工具结果正文只留开头：导出的版面是给人抽查的，不把整个结果塞满</summary>
    private const int MaxResultChars = 600;

    /// <summary>一份会话流水 markdown 的开头：标题 + 「## 流水」小节</summary>
    /// <param name="title">会话标题</param>
    /// <returns>开头正文</returns>
    public static string BuildHeading(string title) => $"# {title}\n\n## 流水\n\n";

    /// <summary>按会话短号生成流水文件名（短号防撞名，也避免标题里的非法字符进文件名）</summary>
    /// <param name="shortId">模型可见的短号（真实 ID 前 8 位）</param>
    /// <returns>文件名</returns>
    public static string FileNameOf(string shortId) => $"{FileNamePrefix}-{shortId}.md";

    /// <summary>把一条历史消息格式化成 markdown 段：角色标题 + 思考/正文/工具调用（参数原样）</summary>
    /// <param name="message">历史消息</param>
    /// <returns>markdown 段</returns>
    public static string FormatMessage(ChatMessage message)
    {
        StringBuilder text = new();
        text.AppendLine($"### {message.Role}").AppendLine();
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                case TextReasoningContent reasoning when !string.IsNullOrWhiteSpace(reasoning.Text):
                    text.AppendLine($"> 思考：{reasoning.Text.Trim().Replace("\n", " ")}").AppendLine();
                    break;
                case TextContent plain when !string.IsNullOrWhiteSpace(plain.Text):
                    text.AppendLine(plain.Text.Trim()).AppendLine();
                    break;
                case FunctionCallContent call:
                    text.AppendLine($"- 调用 `{call.Name}`：`{ToJson(call.Arguments)}`");
                    break;
                case FunctionResultContent result:
                    string body = result.Result?.ToString() ?? string.Empty;
                    if (body.Length > MaxResultChars) body = body[..MaxResultChars] + "…";
                    text.AppendLine($"- 结果：{body.Replace("\n", " ")}").AppendLine();
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>流水末尾的用量行，与群流水、开发导出的口径一致</summary>
    /// <param name="inputTokens">累计输入 token</param>
    /// <param name="outputTokens">累计输出 token</param>
    /// <returns>一行</returns>
    public static string FormatUsage(long inputTokens, long outputTokens) =>
        $"\n输入 {inputTokens} · 输出 {outputTokens}";

    private static string ToJson(IDictionary<string, object?>? arguments) =>
        arguments is not { Count: > 0 } ? string.Empty : System.Text.Json.JsonSerializer.Serialize(arguments, ToolJson.Arguments);
}