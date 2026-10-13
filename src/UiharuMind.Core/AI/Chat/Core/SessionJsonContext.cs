using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 会话档案序列化的 source-gen 上下文（ADR 0029 前置之一：JSON source-gen）。
/// <see cref="ChatMessage"/> 历史直接落盘，其 <see cref="AIContent"/> 多态由
/// M.E.AI 在类型上用 [JsonPolymorphic] + [JsonDerivedType] 声明（探针实测），
/// source-gen 生成器会读取这些特性，$type 判别符表与派生映射原样保留——
/// 不需要手抄派生表。形状与语义由 <c>SessionJsonShapeGuardTests</c> 两层护栏兜底。
/// </summary>
/// <remarks>
/// 只保留会话档案要用的类型。SaveUtility（A2，object 通用路由，未知类型容错）不在这里，
/// 两摊口径独立，互不牵连（用户拍板，converter 单点不全局动 object，理由见该决策）。
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(List<ChatMessage>))]
[JsonSerializable(typeof(ChatSession))]
[JsonSerializable(typeof(ChatSessionMeta))]
[JsonSerializable(typeof(List<ChatSessionMeta>))]
// AOT 下会话历史/工具参数里会序列化 string[]/List<string>（探针实锤：缺类型图直接抛，优香/普拉娜定位）
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
// AdditionalProperties 里实际承载的复杂对象（探针发现，反射模式下按运行时类型写，source-gen 需要类型图）
[JsonSerializable(typeof(AgentRequestMessageSourceAttribution))]
internal partial class SessionJsonContext : JsonSerializerContext
{
}