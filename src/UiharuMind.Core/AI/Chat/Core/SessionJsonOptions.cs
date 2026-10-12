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
using System.Text.Json.Serialization.Metadata;
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

    /// <summary>
    /// 会话索引（index.json）的序列化配置：与 <see cref="Default"/> 同源，但不缩进。
    /// 索引整份重写，缩进会让文件成倍膨胀（六百多条时约 950KB → 350KB）。
    /// </summary>
    public static readonly JsonSerializerOptions Index = CreateIndex();

    private static JsonSerializerOptions CreateIndex()
    {
        JsonSerializerOptions options = new(Default) { WriteIndented = false };
        options.MakeReadOnly();
        return options;
    }

    private static JsonSerializerOptions Create()
    {
        // 基座换成 source-gen 上下文（SessionJsonContext）：naming policy / null 忽略 / 枚举
        // 由生成器保证，与反射模式的对应配置一致（护栏验证）。多态判别符表继承 M.E.AI 特性。
        JsonSerializerOptions options = new(SessionJsonContext.Default.Options)
        {
            WriteIndented = true,
            // 读端宽容设置（source-gen 不管这些，但旧档案读取要靠它们）
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            // 中文等非 ASCII 直接写入，避免 \uXXXX 转义导致存档不可读
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        // 单点 converter：只挂 FunctionCallContent.Arguments（object 值写按运行时类型 / 读回 JsonElement），
        // 不全局动 object，避开 SaveUtility 那摊（用户拍板）。
        options.TypeInfoResolver = options.TypeInfoResolver!.WithAddedModifier(ApplyArgumentsConverter);
        options.MakeReadOnly();
        return options;
    }

    /// <summary>给 <see cref="FunctionCallContent.Arguments"/> 属性挂单点 converter</summary>
    private static void ApplyArgumentsConverter(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(FunctionCallContent)) return;
        JsonPropertyInfo? arguments = typeInfo.Properties.FirstOrDefault(p => p.Name == "arguments");
        if (arguments != null) arguments.CustomConverter = new ArgumentsJsonConverter();
    }
}
