/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 从一轮的内容流里数生图张数。生图按张计费、不报 token，所以不进 token 账本，单独累计在会话上。
///
/// 工具结果只带调用编号不带工具名，所以先记下哪些调用是生图，结果来了再按结果里的保存行计数——
/// 与工具卡载缩略图同一份解析（<see cref="ImageGenerationTool.ParseSavedPaths"/>），失败的调用没有保存行，自然不计。
/// 一个实例只管一轮。
/// </summary>
internal sealed class GeneratedImageTally
{
    private readonly HashSet<string> _pendingCalls = new(StringComparer.Ordinal);

    /// <summary>
    /// 看一条内容
    /// </summary>
    /// <param name="content">内容流里的一条</param>
    /// <returns>这一条带来的新增张数；不是生图结果为 0</returns>
    public int Observe(AIContent content)
    {
        switch (content)
        {
            case FunctionCallContent { Name: ImageGenerationTool.ToolName } call:
                _pendingCalls.Add(call.CallId);
                return 0;

            // 认过一次就摘掉:同一个结果再出现(重放、补发)不重复计
            case FunctionResultContent result when _pendingCalls.Remove(result.CallId):
                return ImageGenerationTool.ParseSavedPaths(result.Result?.ToString()).Count;

            default:
                return 0;
        }
    }
}
