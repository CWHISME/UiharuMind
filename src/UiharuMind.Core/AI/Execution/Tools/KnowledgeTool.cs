/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 知识库检索工具:模型按需以聚焦的查询词检索会话绑定的知识库(锁单库),
/// 取代"每轮拿用户原话强制检索一次"的被动注入——查询词质量与调用时机都交给模型。
/// 只读能力,无需审批。
///
/// 工具名刻意不含 memory 字样:模型同时看得见框架的 <c>file_memory_*</c>(它自己的笔记),
/// 两边都叫"记忆"时选错的概率很高,而选错的表现是检索不到却不报错——最难查的那类。
/// </summary>
public static class KnowledgeTool
{
    /// <summary>工具名。提示词里提到本工具时一律引用这个常量,写死字面量迟早对不上</summary>
    public const string ToolName = "KnowledgeSearch";

    /// <summary>
    /// 创建知识库检索 AIFunction
    /// </summary>
    /// <param name="knowledgeSource">当前挂接会话的知识库来源(执行时解析,跨会话复用 handle 也正确)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(Func<MemoryData?>? knowledgeSource)
    {
        // 手写 AotToolFunction（A2 全量扩展）：显式 schema + handler
        return new AotToolFunction(ToolName,
            "在本会话绑定的知识库（用户的文档，不是你自己的笔记）中检索与查询词相关的段落。",
            AotToolFunction.SchemaFromNode(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject { ["type"] = "string", ["description"] = "简短聚焦的查询词，不是用户的原始消息。这是向量检索：关键词比整句话更有效。" },
                },
                ["required"] = new JsonArray(JsonValue.Create("query")),
            }),
            async (args, ct) => await SearchAsync(knowledgeSource,
                AotToolFunction.Marshaller.String(args, "query")!).ConfigureAwait(false));
    }

    private static async Task<string> SearchAsync(Func<MemoryData?>? knowledgeSource, string query)
    {
        MemoryData? knowledge = knowledgeSource?.Invoke();
        if (knowledge == null) return "本会话没有绑定知识库。";
        if (string.IsNullOrWhiteSpace(query)) return "查询词不能为空。";

        try
        {
            string snippets = await knowledge.GetLongTermMemory(query).ConfigureAwait(false);
            return string.IsNullOrEmpty(snippets) ? "(未找到相关段落)" : snippets;
        }
        catch (Exception e)
        {
            Log.Warning($"Knowledge search failed: {e.Message}");
            return $"Knowledge search failed: {e.Message}";
        }
    }
}
