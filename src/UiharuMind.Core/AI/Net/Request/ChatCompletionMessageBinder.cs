/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Reasoning;

// 这个文件同时用到 OpenAI.Chat 与 M.E.AI，ChatMessage 两边都有——显式指明用 M.E.AI 那个
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.AI.Net.Request;

/// <summary>
/// 把一条历史消息复刻成 <b>OpenAI SDK 的消息对象</b>，顺手把它的思考写进同一个对象里。
///
/// <para><b>为什么能这么做</b>（ADR 0101）：<c>OpenAI.Chat.ChatMessage</c> 没有公开的
/// <c>SerializedAdditionalRawData</c>，但有 <c>Patch</c>（<see cref="JsonPatch"/>）；
/// 而 M.E.AI 的 <c>ToOpenAIChatMessages</c> 见到 <see cref="ChatMessage.RawRepresentation"/>
/// 是 OpenAI 的消息对象就<b>原样发出去</b>。两件事合起来就是注入点：
/// 自己造一条消息、把 <c>reasoning_content</c> / <c>reasoning_details</c> 用
/// <see cref="JsonPatch.Set(ReadOnlySpan{byte}, BinaryData)"/> 写进它，
/// 思考就<b>长在这条消息上</b>，wire 上再没有「第几条」这回事。</para>
///
/// <para><b>只接管能复刻准的消息</b>：本类复刻的是 M.E.AI 的
/// <c>ToOpenAIChatMessages</c> 里 assistant 那一支。凡是它做的事我们没做全的
/// （附件、拒绝），一律返回 null 交回 M.E.AI，这一轮这条消息不回填思考——
/// 宁缺毋滥，缺字段服务端能自愈，配错查不出来。</para>
/// </summary>
internal static partial class ChatCompletionMessageBinder
{
    /// <summary>
    /// M.E.AI 清洗作者名用的口径：只留 <c>[a-zA-Z0-9_]</c>，其余全删，超 64 截断。
    ///
    /// <para>必须照抄，不能图省事跳过——本仓<b>每条消息都带 <c>AuthorName</c></b>
    /// （<c>ChatSession.CreateMessage</c> 按角色填人名），跳过就等于「assistant 消息一律不接管」，
    /// 回填整个失效。而中文名清洗后是空串，最终落成 <c>null</c>、wire 上不出现 <c>name</c>。</para>
    /// </summary>
    private const string InvalidAuthorNamePattern = @"[^a-zA-Z0-9_]+";

    [GeneratedRegex(InvalidAuthorNamePattern)]
    private static partial Regex InvalidAuthorNameRegex();

    /// <summary>转义后的思考字面量，键是正文那一个字符串实例（见 <see cref="EscapedLiteral"/>）</summary>
    private static readonly ConditionalWeakTable<string, BinaryData> EscapedLiterals = new();

    /// <summary>patch 路径的字节，键是 wire 键名（每请求一个，通常只有一两个）</summary>
    private static readonly ConcurrentDictionary<string, byte[]> ReasoningPaths = new(StringComparer.Ordinal);

    /// <summary>工具参数的 JSON，键是那次调用的参数字典实例（见 <see cref="SerializeArguments"/>）</summary>
    private static readonly ConditionalWeakTable<IDictionary<string, object?>, BinaryData> ArgumentJsons = new();

    /// <summary>无参调用的参数（M.E.AI 把它写成 JSON 的 <c>null</c>，不是 <c>{}</c>）</summary>
    private static readonly BinaryData NullArguments = BinaryData.FromBytes(
        JsonSerializer.SerializeToUtf8Bytes((IDictionary<string, object?>?)null,
            AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>))));

    /// <summary>
    /// 给一条助手消息挂上思考，返回<b>新的</b>消息对象（不原地改）。
    /// </summary>
    /// <param name="message">历史消息</param>
    /// <param name="reasoningText">要回填的思考正文；null 表示这条没有正文</param>
    /// <param name="ticketJson">要回填的加密票据原文；null 表示没有</param>
    /// <param name="reasoningField">思考正文的 wire 键名（模型声明，见 <see cref="ReasoningReplayPolicy"/>)</param>
    /// <returns>挂好思考的新消息；这条消息复刻不了时为 null</returns>
    public static ChatMessage? Attach(ChatMessage message, string? reasoningText, string? ticketJson, string reasoningField)
    {
        AssistantChatMessage? wire = ToWireMessage(message);
        if (wire is null) return null;

        PatchReasoning(wire, reasoningText, ticketJson, reasoningField);

        // 不原地改：历史里那条是持久对象，不该被塞进一个 SDK 运行时引用
        // （ChatMessage.Clone() 会把它一起带走，下一轮容易读到上一轮的快照）
        ChatMessage bound = ChatMessageCopies.WithContents(message, [.. message.Contents]);
        bound.RawRepresentation = wire;
        return bound;
    }

    /// <summary>
    /// 复刻 M.E.AI 的 assistant 消息转换。返回 null 表示「这条我们造不出与它一模一样的东西」。
    /// </summary>
    /// <param name="message">历史消息</param>
    /// <returns>wire 消息；复刻不了时为 null</returns>
    private static AssistantChatMessage? ToWireMessage(ChatMessage message)
    {
        if (message.Role != ChatRole.Assistant) return null;

        List<ChatMessageContentPart>? parts = null;
        List<ChatToolCall>? toolCalls = null;
        foreach (AIContent content in message.Contents)
        {
            switch (content)
            {
                // 思考不进正文，它走 Patch
                case TextReasoningContent:
                    break;
                case TextContent text:
                    (parts ??= []).Add(ChatMessageContentPart.CreateTextPart(text.Text));
                    break;
                case FunctionCallContent call:
                    (toolCalls ??= []).Add(ChatToolCall.CreateFunctionToolCall(call.CallId, call.Name, SerializeArguments(call)));
                    break;
                default:
                    // 附件、拒绝、自定义内容：M.E.AI 有它自己的一套映射，我们不复刻
                    return null;
            }
        }

        // 下面三种分支与 M.E.AI 一一对应，连「没有内容时补一个空文本段」都一样——
        // 差一个字节，这条消息的前缀缓存就与上一轮对不上
        AssistantChatMessage wire;
        if (parts is not null)
        {
            wire = new AssistantChatMessage(parts);
            if (toolCalls is not null)
            {
                foreach (ChatToolCall toolCall in toolCalls) wire.ToolCalls.Add(toolCall);
            }
        }
        else
        {
            wire = toolCalls is not null
                ? new AssistantChatMessage(toolCalls)
                : new AssistantChatMessage([ChatMessageContentPart.CreateTextPart(string.Empty)]);
        }

        wire.ParticipantName = SanitizeAuthorName(message.AuthorName);
        return wire;
    }

    /// <summary>
    /// 作者名清洗，与 M.E.AI 的 <c>SanitizeAuthorName</c> 逐字一致。
    /// 中文名（本仓角色卡基本是中文）清洗后为空，落成 null——wire 上不出现 <c>name</c>。
    /// </summary>
    /// <param name="name">原始作者名</param>
    /// <returns>清洗后的名字；清不出东西时为 null</returns>
    private static string? SanitizeAuthorName(string? name)
    {
        if (name is null) return null;

        const int maxLength = 64;
        name = InvalidAuthorNameRegex().Replace(name, string.Empty);
        if (name.Length == 0) return null;

        return name.Length > maxLength ? name[..maxLength] : name;
    }

    /// <summary>
    /// 工具参数按 M.E.AI 同一口径序列化：<c>AIJsonUtilities.DefaultOptions</c> 对
    /// <c>IDictionary&lt;string, object?&gt;</c> 的类型信息。换一套序列化选项，
    /// 同一份参数就编出不同字节，前缀缓存会从这条消息起分叉。
    ///
    /// <para>按参数字典实例缓存：一次工具调用的参数建好之后只被读（本仓没有任何地方就地改它），
    /// 而它每轮都要重新编一遍。带 <c>Edit</c>/<c>Write</c> 的长会话里，参数本身就有几 KB，
    /// 95 轮攒下来是实打实的一笔。</para>
    /// </summary>
    /// <param name="call">工具调用内容</param>
    /// <returns>参数 JSON</returns>
    private static BinaryData SerializeArguments(FunctionCallContent call) =>
        call.Arguments is not { } arguments
            ? NullArguments
            : ArgumentJsons.TryGetValue(arguments, out BinaryData? cached)
                ? cached
                : ArgumentJsons.GetValue(arguments, static value => BinaryData.FromBytes(
                    JsonSerializer.SerializeToUtf8Bytes(value,
                        AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IDictionary<string, object?>)))));

    /// <summary>
    /// 把思考写进消息对象的未知字段区（<c>Patch</c>）。
    ///
    /// 字段顺序与旧做法一致（先正文后票据），字节层面因此与升级前基本一致——
    /// 升级后该会话的前缀缓存只冷一次。
    /// </summary>
    /// <param name="wire">wire 消息</param>
    /// <param name="reasoningText">思考正文</param>
    /// <param name="ticketJson">票据原文</param>
    /// <param name="reasoningField">思考正文的 wire 键名</param>
    private static void PatchReasoning(AssistantChatMessage wire, string? reasoningText, string? ticketJson,
        string reasoningField)
    {
#pragma warning disable SCME0001 // JsonPatch 是实验 API：M.E.AI 自己也在用它读响应，短期内不会消失
        if (reasoningText is not null)
        {
            wire.Patch.Set(PathOf(reasoningField), EscapedLiteral(reasoningText));
        }

        // 票是原样切下来的 JSON 数组，回填时也原样贴回去
        if (ticketJson is not null)
        {
            wire.Patch.Set("$.reasoning_details"u8, BinaryData.FromString(ticketJson));
        }
#pragma warning restore SCME0001
    }

    /// <summary>
    /// 思考正文转义成<b>带引号的 JSON 字面量</b>，按正文那一个字符串实例缓存。
    ///
    /// <para><b>为什么值得缓存</b>：一条消息的思考只产一次（ADR 0100 不变量 2），
    /// 而它每轮都要被塞进请求体。长会话里这是请求侧最大的一笔分配——
    /// 实测 95 轮 × 3000 字，光「转义 + 拷成 <see cref="BinaryData"/>」每发就要 1.4 MB，
    /// 而字节一个都不会变。</para>
    ///
    /// <para><b>为什么键是字符串实例而不是内容对象</b>：正文是<b>不可变</b>的字符串，
    /// 存进消息之后没人重新赋值，所以同一个实例每轮都命中；一旦有人真的换了正文，
    /// 那是另一个实例，自然落空、重新转义——<b>不会发出旧内容</b>。
    /// 弱引用键，消息被回收时缓存跟着走。</para>
    /// </summary>
    /// <param name="text">思考正文</param>
    /// <returns>带引号的 JSON 字面量</returns>
    private static BinaryData EscapedLiteral(string text) =>
        EscapedLiterals.TryGetValue(text, out BinaryData? cached)
            ? cached
            : EscapedLiterals.GetValue(text, static value => BinaryData.FromString(
                // 走 GetTypeInfo 而不是反射重载：本仓开着裁剪/AOT 分析器，反射序列化会报警
                JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(string)))));

    /// <summary>
    /// 思考正文的 patch 路径（<c>$.reasoning_content</c>）。键名每请求一个，缓存掉逐条消息的编码。
    /// </summary>
    /// <param name="reasoningField">思考正文的 wire 键名</param>
    /// <returns>路径的 UTF-8 字节</returns>
    private static byte[] PathOf(string reasoningField) =>
        ReasoningPaths.GetOrAdd(reasoningField, static field => Encoding.UTF8.GetBytes($"$.{field}"));
}
