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

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 长期记忆检索工具:模型按需以聚焦的查询词检索会话绑定的记忆库(锁单库),
/// 取代"每轮拿用户原话强制检索一次"的被动注入——查询词质量与调用时机都交给模型。
/// 只读能力,无需审批。
/// </summary>
public static class MemoryTool
{
    /// <summary>
    /// 创建记忆检索 AIFunction
    /// </summary>
    /// <param name="memorySource">当前挂接会话的记忆库来源(执行时解析,跨会话复用 handle 也正确)</param>
    /// <returns>工具实例</returns>
    public static AITool Create(Func<MemoryData?>? memorySource)
    {
        return AIFunctionFactory.Create(
            async ([Description("A focused query describing what to recall from long-term memory.")] string query,
                    CancellationToken cancellationToken = default) =>
                await SearchAsync(memorySource, query).ConfigureAwait(false),
            "memory_search",
            "Search the session's long-term memory library for information relevant to the query. " +
            "Use a focused query, not the user's raw message.");
    }

    private static async Task<string> SearchAsync(Func<MemoryData?>? memorySource, string query)
    {
        MemoryData? memory = memorySource?.Invoke();
        if (memory == null) return "No memory library is bound to this session.";
        if (string.IsNullOrWhiteSpace(query)) return "Query must not be empty.";

        try
        {
            string snippets = await memory.GetLongTermMemory(query).ConfigureAwait(false);
            return string.IsNullOrEmpty(snippets) ? "(no relevant memory found)" : snippets;
        }
        catch (Exception e)
        {
            Log.Warning($"Memory search failed: {e.Message}");
            return $"Memory search failed: {e.Message}";
        }
    }
}
