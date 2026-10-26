/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.InteropServices;

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 设置页「模型 Shell」的预设目录与生效解析（公共只读入口，装配链路不依赖它）。
///
/// 装配侧不经过这里：<see cref="ShellExecutorFactory.Create"/> 只认装配计划里固化好的
/// <c>ShellBinaryPath</c>（<see cref="AgentAssemblyPlan"/>）。本类服务的是 UI 侧两件事——
/// 把常见 shell 列成预设（探测不中的不列出，用户可走自定义路径），以及报告「当前配置实际
/// 生效的是哪个 shell」给探测按钮用。两处口径都直接读框架的 <c>ResolvedShellBinary</c>，
/// 不自己复制 SDK 的解析逻辑。
/// </summary>
public static class ShellPresetCatalog
{
    /// <summary>
    /// 一个可选 shell 预设。<see cref="BinaryPath"/> 恒非空（探测到才列出）；
    /// 「自动 / 自定义」是 UI 侧的逻辑项，不在这个列表里。
    /// </summary>
    /// <param name="Id">稳定标识（如 <c>git-bash</c>），UI 判重与匹配用</param>
    /// <param name="Name">展示名（如 <c>Git Bash</c>）</param>
    /// <param name="BinaryPath">shell 可执行文件的绝对路径</param>
    public sealed record ShellPreset(string Id, string Name, string BinaryPath);

    /// <summary>
    /// 当前平台探测到的常见 shell 预设，按优先顺序排。
    /// Windows：Git Bash / PowerShell 7 / Windows PowerShell / cmd.exe；
    /// POSIX：/bin/bash / /bin/sh。只列本机确实存在的。
    /// </summary>
    public static IReadOnlyList<ShellPreset> DetectForCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            List<ShellPreset> presets = new();
            AddFirstExisting(presets, "git-bash", "Git Bash",
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files\Git\usr\bin\bash.exe");
            AddFirstExisting(presets, "pwsh", "PowerShell 7 (pwsh)", FindOnPath("pwsh.exe"));
            AddFirstExisting(presets, "powershell", "Windows PowerShell",
                Path.Combine(SystemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"));
            AddFirstExisting(presets, "cmd", "cmd.exe", Path.Combine(SystemRoot, "System32", "cmd.exe"));
            return presets;
        }

        List<ShellPreset> posix = new();
        AddFirstExisting(posix, "bash", "bash", "/bin/bash");
        AddFirstExisting(posix, "sh", "sh", "/bin/sh");
        return posix;
    }

    /// <summary>
    /// 当前配置下实际生效的 shell 二进制（空配置 = 框架系统默认）。给设置页探测按钮展示用。
    /// </summary>
    /// <param name="configuredPath">配置的覆盖路径；空或 null 表示跟随系统默认</param>
    /// <returns>生效的 shell 可执行文件路径</returns>
    public static string GetEffectiveShellBinary(string? configuredPath)
    {
        var executor = ShellExecutorFactory.Create(Path.GetTempPath(), null, configuredPath);
        try
        {
            return executor.ResolvedShellBinary;
        }
        finally
        {
            executor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string SystemRoot =>
        Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

    private static void AddFirstExisting(List<ShellPreset> presets, string id, string name, params string?[] candidates)
    {
        string? found = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));
        if (found is not null) presets.Add(new ShellPreset(id, name, found));
    }

    private static string? FindOnPath(string fileName)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
