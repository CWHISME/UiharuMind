/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// shell 执行器的唯一构造口：剥掉宿主注入的诊断变量、显式用 stateless 模式、加前台超时。
///
/// 选 <see cref="ShellMode.Stateless"/> 而不是 SDK 默认的 Persistent：persistent 的常驻 bash
/// 在「用户中途停止对话」时只会取消等待、不会杀掉正在跑的前台命令（见 agent-framework
/// ShellSession.WaitForSentinelAsync 的取消路径），下次调用会排到它后面卡到超时。stateless
/// 每次起新 shell，取消/超时由 SDK KillProcessTree 处理，停止后不留残留进程；代价是
/// export/函数等 shell 状态不跨调用持久（cd 本就由 ConfineWorkingDirectory 锁回初始目录）。
///
/// Rider 调试时会给被调试进程注入 `DOTNET_DiagnosticPorts=...,connect,suspend`，
/// 子 shell 不剥就继承，起的每个 `dotnet` 都在 `ds_server_pause_for_diagnostics_monitor`
/// 里零 CPU 挂起；且 `Timeout` 默认为空即无限等待，一 hang 就全堵。
/// 超过外层超时的任务走后台加轮询（`cmd &gt; log 2&gt;&amp;1 &amp;` 再 `tail`），不受此前台上限影响；
/// 后台那段没把输出整个重定向走时，框架会在超时之外一直等输出管道关闭，由 <see cref="ShellHangGuardFunction"/> 兜底。
/// </summary>
internal static class ShellExecutorFactory
{
    /// <summary>shell 一次调用的前台上限；后台任务工具的说明拿它划分两把工具</summary>
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HangGrace = TimeSpan.FromSeconds(30); //框架自己的超时先到，兜底只接它接不住的那种

    private static readonly string[] StrippedVariables =
    [
        "DOTNET_DiagnosticPorts",
        "DOTNET_DefaultDiagnosticPortSuspend",
    ];

    // [MFA绕坑] 绕:从默认工具描述里剥掉这句 因:它写死在 LocalShellExecutor 私有的 BuildDefaultDescription 末尾,自动档与预授权下并不成立 删除条件:框架按审批配置决定是否输出
    private const string ApprovalClaim = "The user reviews and approves every call.";

    private static readonly string BackgroundTasksHint =
        $"Commands expected to run over {CommandTimeout.TotalMinutes:0} minutes or never exit: use " +
        Tools.BackgroundTasks.BackgroundTaskTool.ToolName + ".";

    /// <summary>
    /// 把执行器包成模型可调的 shell 工具（主代理与子代理共用）。
    /// 审批是给用户看的机制，模型知道了也不改变行为（ADR 0017 第九节）；而自动档下那句还是假的，
    /// 会让模型以为总有人替它把关破坏性命令。
    /// </summary>
    /// <param name="executor">shell 执行器</param>
    /// <param name="hasBackgroundTasks">同一会话是否挂了后台任务工具：挂了就把长命令指过去</param>
    /// <returns>需审批的 shell 工具</returns>
    public static AIFunction CreateTool(LocalShellExecutor executor, bool hasBackgroundTasks = false)
    {
        string description = executor.AsAIFunction(CharacterRunnerFactory.ShellToolName).Description
            .Replace(ApprovalClaim, string.Empty, StringComparison.Ordinal)
            .TrimEnd();
        if (hasBackgroundTasks) description += " " + BackgroundTasksHint;
        AIFunction gated = executor.AsAIFunction(CharacterRunnerFactory.ShellToolName, description);
        return new ApprovalRequiredAIFunction(new ShellHangGuardFunction(gated, CommandTimeout + HangGrace,
            hasBackgroundTasks ? ShellHangGuardFunction.HangNoticeWithBackgroundTasks : ShellHangGuardFunction.HangNotice));
    }

    public static LocalShellExecutor Create(
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment)
    {
        return new LocalShellExecutor(new LocalShellExecutorOptions
        {
            Mode = ShellMode.Stateless,
            WorkingDirectory = workingDirectory,
            Environment = BuildEnvironment(environment),
            Timeout = CommandTimeout,
        });
    }

    /// <summary>
    /// shell 子进程的环境覆盖表：追加项照搬，宿主注入的诊断变量置 null（即删除）。后台任务起进程也用这一份
    /// </summary>
    /// <param name="environment">追加环境，可空</param>
    /// <returns>覆盖表，值为 null 表示从继承的环境里删掉</returns>
    internal static Dictionary<string, string?> BuildEnvironment(IReadOnlyDictionary<string, string?>? environment)
    {
        Dictionary<string, string?> merged = environment is null
            ? new(StringComparer.Ordinal)
            : new(environment, StringComparer.Ordinal);
        foreach (string name in StrippedVariables)
            merged[name] = null;
        return merged;
    }
}
