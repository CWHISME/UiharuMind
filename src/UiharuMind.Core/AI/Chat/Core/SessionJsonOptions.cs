/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 会话持久化专用的序列化配置。
/// 会话历史直接存 <see cref="Microsoft.Extensions.AI.ChatMessage"/>，其 <see cref="AIContent"/> 为多态类型，
/// 需要 Microsoft.Extensions.AI 提供的 TypeInfoResolver 才能正确写入与还原 $type 判别符。
/// 不复用 SaveUtility 的通用配置：后者服务角色卡、设置、记忆等全部存档，
/// 其 UnknownTypeHandling 等设置是那些类型的承重项，不应为会话改动。
/// </summary>
public static class SessionJsonOptions
{
    /// <summary>
    /// 会话本体的序列化配置
    /// </summary>
    public static readonly JsonSerializerOptions Default = Create();

    private static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            // 中文等非 ASCII 直接写入，避免 \uXXXX 转义导致存档不可读
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.MakeReadOnly();
        return options;
    }
}
