/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.ComponentModel;
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
    public const string ToolName = "knowledge_search";

    /// <summary>
    /// 创建知识库检索 AIFunction
    /// </summary>
    /// <param name="knowledgeSource">当前挂接会话的知识库来源(执行时解析,跨会话复用 handle 也正确)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(Func<MemoryData?>? knowledgeSource)
    {
        return AIFunctionFactory.Create(
            async ([Description("A focused query describing what to look up in the knowledge base.")]
                    string query,
                    CancellationToken cancellationToken = default) =>
                await SearchAsync(knowledgeSource, query).ConfigureAwait(false),
            ToolName,
            "Search the knowledge base attached to this session for passages relevant to the query. " +
            "Use a focused query, not the user's raw message. " +
            "This searches the user's document collection, not your own notes.");
    }

    private static async Task<string> SearchAsync(Func<MemoryData?>? knowledgeSource, string query)
    {
        MemoryData? knowledge = knowledgeSource?.Invoke();
        if (knowledge == null) return "No knowledge base is attached to this session.";
        if (string.IsNullOrWhiteSpace(query)) return "Query must not be empty.";

        try
        {
            string snippets = await knowledge.GetLongTermMemory(query).ConfigureAwait(false);
            return string.IsNullOrEmpty(snippets) ? "(no relevant passage found)" : snippets;
        }
        catch (Exception e)
        {
            Log.Warning($"Knowledge search failed: {e.Message}");
            return $"Knowledge search failed: {e.Message}";
        }
    }
}
