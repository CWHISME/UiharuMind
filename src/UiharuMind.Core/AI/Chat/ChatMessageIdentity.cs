/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 消息的<b>稳定标识</b>：界面条目与历史消息靠它认人，而不是靠对象引用或显示文本。
///
/// <para>
/// 模型回复的 <see cref="ChatMessage.MessageId"/> 由服务端给（OpenAI 兼容线的 <c>id</c> 字段，
/// 实测 99% 的助手消息都有）。我们自己造的消息没有——用户输入、开场白、交接文档、
/// 检索片段、取消补存，真实档案里用户消息的覆盖率是 <b>0%</b>。所以这里补上：
/// 创建时盖（界面在落盘前就要认它），落盘时兜底（老存档、别处造的漏网消息）。
/// </para>
/// <para>
/// 它<b>不进请求体</b>：实测给消息盖上之后发出去的字节与不盖时逐字相同，前缀缓存不受影响。
/// </para>
/// </summary>
public static class ChatMessageIdentity
{
    /// <summary>
    /// 取这条消息的标识；没有就盖一个并返回。
    ///
    /// <para>
    /// 幂等：已有标识原样返回（服务端给的、创建时盖的都不动），所以可以在创建处与落盘处各调一次。
    /// 有时间戳的老消息按 <c>(角色, CreatedAt, 正文)</c> 确定性生成——随机 Guid 只在单次进程内唯一，
    /// 冷卸载/跨进程换实例后同一条消息会算出不同 id，判重失配就会重复画一条。
    /// </para>
    /// </summary>
    /// <param name="message">消息（就地修改）</param>
    /// <returns>稳定标识</returns>
    public static string Ensure(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.MessageId)) return message.MessageId;
        message.MessageId = FallbackId(message);
        return message.MessageId;
    }

    /// <summary>
    /// 缺标识消息的回落键：角色 + 时间戳 + 正文的稳定哈希。
    ///
    /// <para>
    /// <c>local-</c> 前缀与服务端 id（GUID / <c>chatcmpl-*</c>）分开，不占用真实 id 的空间。
    /// 真实档案里这个三元组在<b>同一会话内</b> 0 碰撞；没有时间戳的消息退回随机 id，
    /// 免得两条逐字相同的消息被合成一条。用户消息现在也在创建时盖了标识，这条回落只服务老存档。
    /// </para>
    /// </summary>
    private static string FallbackId(ChatMessage message)
    {
        // 没有时间戳就没有可用的稳定判别量：硬按正文生成会让同一会话里两条逐字相同的消息
        // 共用一个 id、被判重吞掉一条。宁可这种消息退回随机 Guid（它与正文相同的另一条绝不撞），
        // 也不破坏「两条消息是两条」这条底线。真实档案里 96% 的缺 id 消息都有时间戳
        if (message.CreatedAt is not { } createdAt) return NewId();

        string stamp = createdAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        string role = message.Role.Value ?? string.Empty;
        string body = string.Join("\u001e", message.Contents.Select(content => content switch
        {
            TextContent text => text.Text,
            TextReasoningContent reasoning => reasoning.Text,
            _ => content.GetType().Name,
        }));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{role}\u001f{stamp}\u001f{body}"));
        return "local-" + Convert.ToHexString(hash, 0, 16);
    }

    /// <summary>造一个新的消息标识</summary>
    /// <returns>32 位十六进制串，与框架自己生成的那种同形</returns>
    public static string NewId() => Guid.NewGuid().ToString("N");
}
