/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

/// <summary>
/// 后台任务的起跑环境：与同一会话的 Shell 工具同一个 shell、同一个工作目录、同一份环境，
/// 模型在 Shell 里试通的命令挪过来照样能跑
/// </summary>
/// <param name="ShellBinary">shell 可执行文件（取自 Shell 执行器）</param>
/// <param name="WorkingDirectory">工作目录</param>
/// <param name="Environment">环境覆盖表，值为 null 表示删除（见 <see cref="ShellExecutorFactory.BuildEnvironment"/>）</param>
/// <param name="LogDirectory">任务日志目录</param>
public sealed record BackgroundTaskLaunch(
    string ShellBinary,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?> Environment,
    string LogDirectory)
{
    /// <summary>
    /// 按 shell 种类给出执行一条命令的参数。
    ///
    /// [MFA绕坑] 绕:照抄框架 ResolvedShell.StatelessArgvForCommand 的口径 因:它与 ShellKind 都是 internal
    /// 删除条件:框架公开按命令取参数的入口
    /// </summary>
    /// <param name="command">命令原文</param>
    /// <returns>参数列表</returns>
    public IReadOnlyList<string> ArgumentsFor(string command) =>
        Path.GetFileNameWithoutExtension(ShellBinary).ToUpperInvariant() switch
        {
            "PWSH" or "POWERSHELL" => ["-NoProfile", "-NoLogo", "-NonInteractive", "-Command", command],
            "CMD" => ["/d", "/c", command],
            "BASH" => ["--noprofile", "--norc", "-c", command],
            _ => ["-c", command],
        };

    /// <summary>是不是 PowerShell：它的重定向默认不是 UTF-8，要另设</summary>
    public bool IsPowerShell => Path.GetFileNameWithoutExtension(ShellBinary).ToUpperInvariant() is "PWSH" or "POWERSHELL";
}
