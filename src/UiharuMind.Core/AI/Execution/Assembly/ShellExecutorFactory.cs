/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;

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

    // [MFA绕坑] 绕:工具描述与参数说明自写中文,不取框架默认的英文文案 因:主力模型中文语料为主,
    // 且框架文案写死的审批句在自动档下不成立;自写后随框架升级需人工核对行为(超时/截断口径)
    // 删除条件:框架描述可配置语言或团队决定统一英文

    private static readonly string BackgroundTasksHint =
        $"预期要跑超过 {CommandTimeout.TotalMinutes:0} 分钟或不会退出的命令：用 " +
        Tools.BackgroundTasks.BackgroundTaskTool.ToolName + "，它结束时会主动通知你。" +
        // 实机抽样:单会话前台 sleep 累计上百分钟(sleep 900、sleep 420 + tail 盯后台日志)
        "不要在这里用 sleep 干等或轮询它的输出。";

    /// <summary>
    /// 把执行器包成模型可调的 shell 工具（主代理与子代理共用）。
    /// 审批是给用户看的机制，模型知道了也不改变行为（ADR 0017 第九节）；描述自写中文，
    /// 不取框架默认（其末尾写死的审批句在自动档下不成立，且英文对小模型不友好）。
    /// </summary>
    /// <param name="executor">shell 执行器</param>
    /// <param name="hasBackgroundTasks">同一会话是否挂了后台任务工具：挂了就把长命令指过去</param>
    /// <returns>需审批的 shell 工具</returns>
    public static AIFunction CreateTool(LocalShellExecutor executor, bool hasBackgroundTasks = false)
    {
        string description = BuildDescription(executor);
        AIFunction gated = executor.AsAIFunction(CharacterRunnerFactory.ShellToolName, description);
        // 框架的参数说明写死在方法签名里,只能靠 schema 改写层换文案(与浏览器组 view 参数同款)
        gated = new ParameterDescriptionFunction(gated, "command",
            "要执行的 shell 命令，本机 shell 语法。" +
            (hasBackgroundTasks ? " " + BackgroundTasksHint : string.Empty));
        return new ApprovalRequiredAIFunction(new ShellSelfKillGuardFunction(new ShellHangGuardFunction(gated, CommandTimeout + HangGrace,
            hasBackgroundTasks ? ShellHangGuardFunction.HangNoticeWithBackgroundTasks : ShellHangGuardFunction.HangNotice)));
    }

    private static string BuildDescription(LocalShellExecutor executor)
    {
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "POSIX";
        string binary = executor.ResolvedShellBinary;
        StringBuilder sb = new($"在本地机器上执行一条 shell 命令，返回它的标准输出、标准错误与退出码。操作系统：{os}。");
        // binary 读不到就整句省掉:宁可不说,也不能给模型一个错的 shell
        if (binary.Length > 0) sb.Append($"shell 可执行文件：'{binary}'");
        sb.Append("无状态模式：每次调用都开一个全新 shell，工作目录与环境变量不跨调用保留——需要状态就把相关步骤合成一条命令。")
          .Append($"单次调用超时：{CommandTimeout.TotalSeconds:0} 秒；输出过长时按首尾截断。");
        return sb.ToString();
    }

    public static LocalShellExecutor Create(
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        string? shellBinaryPath = null)
    {
        return new LocalShellExecutor(new LocalShellExecutorOptions
        {
            Mode = ShellMode.Stateless,
            WorkingDirectory = workingDirectory,
            Environment = BuildEnvironment(environment),
            Timeout = CommandTimeout,
            // 非空则覆盖 shell（优先级：显式路径 → AGENT_FRAMEWORK_SHELL → 系统默认）。
            // 覆盖值取自装配计划（AgentAssemblyPlan.Resolve 读配置），这里只认输入、不碰单例。
            Shell = string.IsNullOrWhiteSpace(shellBinaryPath) ? null : shellBinaryPath,
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
