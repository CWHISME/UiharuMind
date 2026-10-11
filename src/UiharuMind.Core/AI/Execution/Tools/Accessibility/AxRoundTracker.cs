/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Tools.Accessibility;

/// <summary>
/// 记住「这一轮」开局时的编号表，给 <see cref="AxRefReconciler"/> 当模型看到的那份。
/// 同一条回复里的调用是并行发出、排队执行的：谁先抢到锁谁记下开局的编号表。调用方自己保证串行
/// </summary>
internal sealed class AxRoundTracker
{
    private object? _round;

    /// <summary>这一轮开始时的编号表：模型写这条回复时看到的</summary>
    public IReadOnlyDictionary<string, AxNode> Refs { get; private set; } = new Dictionary<string, AxNode>();

    /// <summary>
    /// 此刻这次工具调用属于哪一轮：同一条回复里的调用共享框架的同一份历史与同一个迭代号
    /// </summary>
    /// <returns>轮次标识（按值比较）；不在框架的工具调用里（开发脚本直调）为 null</returns>
    public static object? Current() =>
        FunctionInvokingChatClient.CurrentContext is { } context ? (context.Messages, context.Iteration) : null;

    /// <summary>
    /// 进入此刻这一轮：是新的一轮就以此刻的编号表为开局；不在一轮里就每次自成一轮
    /// </summary>
    /// <param name="refs">此刻的编号表</param>
    public void Enter(IReadOnlyDictionary<string, AxNode> refs)
    {
        object? round = Current();
        if (round != null && Equals(round, _round)) return;
        _round = round;
        Refs = refs;
    }
}
