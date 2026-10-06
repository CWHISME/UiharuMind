using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群流水的 markdown 文本：一个来源、两处用——<c>group.dump</c>（App 层的开发导出）与
/// 离席的群流水文件（<see cref="Away.GroupLogFile"/>，给化身第三方视角自读）。\n\n
/// 发言人标注在这里定稿：化身的话标「用户（化身）」，用户真身标「用户」——\n
/// 化身读这份文件能一眼认出哪些是自己的回声，哪些是用户真身的态度
/// </summary>
public static class GroupLogText
{
    /// <summary>群流水文件固定名（草稿目录里）：<see cref="Away.GroupLogFile"/> 用它落盘，产物枚举排除它</summary>
    public const string FileName = "群流水.md";

    /// <summary>一份流水 markdown 的开头：标题 + 「## 流水」小节</summary>
    /// <param name="title">群名</param>
    /// <returns>开头正文</returns>
    public static string BuildHeading(string title) => $"# {title}\n\n## 流水\n\n";

    /// <summary>一群发言的段头：`## 流水 @ #K-M`，化身锚点与人工翻阅都靠它认分段</summary>
    /// <param name="firstIndex">段内第一条发言的群历史下标（0-based，人读 +1）</param>
    /// <param name="lastIndex">段内最后一条发言的群历史下标</param>
    /// <returns>段头一行 + 空行</returns>
    public static string BuildSegmentHeader(int firstIndex, int lastIndex) =>
        $"## 流水 @ #{firstIndex + 1}-{lastIndex + 1}\n\n";

    /// <summary>一条群发言在流水里的样子，与 group.dump 同一口径</summary>
    /// <param name="post">群发言</param>
    /// <returns>「### 发言人 + 正文」的 markdown 段</returns>
    public static string FormatPost(ChatMessage post)
    {
        StringBuilder text = new();
        text.AppendLine($"### {SpeakerLabel(post)}").AppendLine();
        text.AppendLine(post.Text.Trim()).AppendLine();
        return text.ToString();
    }

    /// <summary>一条群发言的发言人标签：离席回执 / 化身（用户名义）/ 用户真身 / 成员角色名</summary>
    /// <param name="post">群发言</param>
    /// <returns>标签</returns>
    public static string SpeakerLabel(ChatMessage post) =>
        ChatMessageAnnotations.GroupAwayReceiptOf(post) != null ? "离席回执"
        : ChatMessageAnnotations.GroupAvatarPostOf(post) != null ? "用户（化身）"
        : post.Role == ChatRole.User ? "用户"
        : post.AuthorName ?? "?";
}