/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI.Tools.Shell;

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
/// 超过外层超时的任务走后台加轮询（`cmd &gt; log 2&gt;&amp;1 &amp;` 再 `tail`），不受此前台上限影响。
/// </summary>
internal static class ShellExecutorFactory
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    private static readonly string[] StrippedVariables =
    [
        "DOTNET_DiagnosticPorts",
        "DOTNET_DefaultDiagnosticPortSuspend",
    ];

    public static LocalShellExecutor Create(
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment)
    {
        Dictionary<string, string?> merged = environment is null
            ? new(StringComparer.Ordinal)
            : new(environment, StringComparer.Ordinal);
        foreach (string name in StrippedVariables)
            merged[name] = null;

        return new LocalShellExecutor(new LocalShellExecutorOptions
        {
            Mode = ShellMode.Stateless,
            WorkingDirectory = workingDirectory,
            Environment = merged,
            Timeout = CommandTimeout,
        });
    }
}
