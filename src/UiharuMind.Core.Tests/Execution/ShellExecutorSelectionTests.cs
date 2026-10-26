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
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// Shell 执行器的选壳：显式路径覆盖框架系统默认（对应设置页「模型 Shell」）。
///
/// 只在 <see cref="ShellExecutorFactory.Create"/> 的输入层面验证——它不碰单例，
/// 配置读取在 <see cref="AgentAssemblyPlan.Resolve"/>（代码审查即可），这里保证
/// 「给了路径就用路径，没给就回退系统默认」这条链路不漂。
/// </summary>
public class ShellExecutorSelectionTests
{
    /// <summary>不传与显式空串都回退系统默认，且解析结果不为空</summary>
    [Fact]
    public void Default_AndExplicitEmpty_ResolveToSameShell()
    {
        string defaultBinary = ResolveBinary(null);
        string emptyBinary = ResolveBinary(string.Empty);

        Assert.False(string.IsNullOrWhiteSpace(defaultBinary));
        Assert.Equal(defaultBinary, emptyBinary);
    }

    /// <summary>显式路径直接成为生效 shell（SDK 只到启动进程时才查文件，这里不必真存在）</summary>
    [Fact]
    public void ExplicitPath_OverridesResolvedShell()
    {
        const string custom = @"C:\Custom\Git\bin\bash.exe";
        Assert.Equal(custom, ResolveBinary(custom));
    }

    /// <summary>工具描述把生效 shell 写给模型看，换了壳描述要跟着变</summary>
    [Fact]
    public async Task ToolDescription_CarriesResolvedShell()
    {
        const string custom = @"C:\Custom\Git\bin\bash.exe";
        await using LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null, custom);

        AIFunction tool = ShellExecutorFactory.CreateTool(executor);
        Assert.Contains(custom, tool.Description);
    }

    /// <summary>后台任务与 Shell 工具同一套口径：bash 路径按 bash 语法起参数</summary>
    [Fact]
    public void BackgroundTaskLaunch_ArgumentsFollowShellKind()
    {
        BackgroundTaskLaunch launch = new(@"C:\Custom\Git\bin\bash.exe", Path.GetTempPath(),
            new Dictionary<string, string?>(), Path.GetTempPath());

        Assert.Equal(["--noprofile", "--norc", "-c", "echo hi"], launch.ArgumentsFor("echo hi"));
        Assert.False(launch.IsPowerShell);
    }

    private static string ResolveBinary(string? overridePath)
    {
        var executor = ShellExecutorFactory.Create(Path.GetTempPath(), null, overridePath);
        try
        {
            return executor.ResolvedShellBinary;
        }
        finally
        {
            executor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
