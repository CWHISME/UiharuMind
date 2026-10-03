using System;
using System.IO;
using System.Linq;
using System.Text;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DevControl;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 内置技能 uiharu-dev（ADR 0061）：开发者模式下教模型经命令行驱动这个应用（ADR 0059）。
/// 正文每次读取时现生成：命令行在哪、通道开没开、有哪些步骤都取那一刻的事实，步骤用法取自各步骤自己的 <see cref="IDevCommand.Usage"/>
/// </summary>
internal static class UiharuDevSkill
{
    public const string Name = "uiharu-dev";

    private const string CliFileName = "UiharuMind.CLI";
    private const string CliEnvironmentVariable = "UIHARU_CLI"; //显式指定命令行路径，优先于自动查找

    /// <summary>
    /// 建这个技能。只在开发者模式开着时出现在目录里
    /// </summary>
    /// <returns>内置技能</returns>
    public static BuiltInSkill Create() => new(Name,
        "Drive this running UiharuMind app from the shell through its developer control channel: create groups, post, start away mode, wait, dump transcripts, read memory stats. Use when asked to operate, test or observe the app itself.",
        BuildBody, () => DeveloperMode.IsEnabled);

    /// <summary>
    /// 生成正文：此刻的事实、用法、注意事项、全部步骤
    /// </summary>
    /// <returns>正文（markdown）</returns>
    internal static string BuildBody()
    {
        DevControlEndpoint endpoint = DevControlEndpoint.Default;
        string? cli = FindCli();
        bool open = File.Exists(endpoint.SocketPath);
        string shell = CharacterRunnerFactory.ShellToolName;

        StringBuilder text = new();
        text.AppendLine("# 驱动 UiharuMind（开发控制通道）");
        text.AppendLine();
        text.AppendLine("你所在的这个应用开着开发控制通道：命令行把一步步操作发给运行中的应用，与用户在界面上点同一条路。");
        text.AppendLine();
        text.AppendLine("## 此刻的事实");
        text.AppendLine();
        text.AppendLine(cli != null
            ? $"- 命令行：`{cli}`"
            : $"- 命令行：没找到。开发构建在 `src/UiharuMind.CLI/bin/<配置>/net10.0/` 下；也可设环境变量 `{CliEnvironmentVariable}` 指过去。找不到就告诉用户，别猜路径。");
        text.AppendLine($"- 档案：`{AppPaths.Root}`（命令行默认就用它；另起一份档案用环境变量 `UIHARU_HOME`）");
        text.AppendLine($"- 通道：{(open ? "开着" : "没开")}（`{endpoint.SocketPath}`）");
        text.AppendLine("- ⚠ 这是真实档案：建的群、发的话、改的东西都是真的。");
        text.AppendLine();
        text.AppendLine("## 用法");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine($"{CliFileName} app call <步骤名> --args '<JSON>'   # 发一步，等它的结果");
        text.AppendLine($"{CliFileName} app run <脚本.jsonl>                  # 一行一步：{{\"op\":\"…\",\"args\":{{…}}}}");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("每步回一行 JSON：`{\"ok\":true,\"elapsedMs\":…,\"result\":…}` 或 `{\"ok\":false,\"error\":\"…\"}`。" +
                        "退出码：0 成功，1 这一步失败，2 连不上或通道关了，3 超时。`--timeout` 是命令行这头等多久（秒）。");
        text.AppendLine();
        text.AppendLine("## 注意");
        text.AppendLine();
        text.AppendLine($"- 长等（`group.wait`、`group.away.wait`、`session.wait`）放进 `{BackgroundTaskTool.ToolName}`：" +
                        $"`{shell}` 单条命令有时限，后台任务跑完会叫醒你。中途看进度用 `group.dump` 导出来读。");
        text.AppendLine("- `page.jump`、`session.*` 作用在主窗口当前显示的那个会话上——你自己就在这个应用里，" +
                        "调它们可能切走或打断你自己这个会话。`app.quit` 会把你自己也关掉。没有用户明说，这几步都别调。");
        text.AppendLine("- 推送、删除、花钱这类收不回的事照常先问用户。");
        text.AppendLine();
        text.AppendLine("## 步骤");
        text.AppendLine();
        foreach (DevStepUsage usage in DevControlHost.HostUsages.Concat(new DevStepExecutor().Usages))
            text.AppendLine($"- `{usage.Op}`：{usage.Usage}");

        return text.ToString();
    }

    /// <summary>
    /// 找命令行：环境变量 → 与应用同目录（打包后）→ 开发构建里 Desktop 产物旁边的 CLI 产物
    /// </summary>
    /// <returns>可执行文件路径；找不到为 null</returns>
    private static string? FindCli()
    {
        string executable = OperatingSystem.IsWindows() ? CliFileName + ".exe" : CliFileName;
        if (Environment.GetEnvironmentVariable(CliEnvironmentVariable) is { Length: > 0 } configured && File.Exists(configured))
            return configured;

        string baseDirectory = AppContext.BaseDirectory;
        string beside = Path.Combine(baseDirectory, executable);
        if (File.Exists(beside)) return beside;

        // macOS 打包（.app）：进程在 <Name>.app/Contents/MacOS/ 下，命令行若随包发在 .app 旁边，顺带看一眼
        int bundleAt = baseDirectory.LastIndexOf(".app" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (bundleAt >= 0 && Path.GetDirectoryName(baseDirectory[..(bundleAt + ".app".Length)]) is { } bundleParent)
        {
            string nextToBundle = Path.Combine(bundleParent, executable);
            if (File.Exists(nextToBundle)) return nextToBundle;
        }

        // 开发构建：…/src/UiharuMind.Desktop/bin/<配置>/<框架>/ → …/src/UiharuMind.CLI/bin/<配置>/<框架>/
        string marker = $"{Path.DirectorySeparatorChar}UiharuMind.Desktop{Path.DirectorySeparatorChar}";
        int at = baseDirectory.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;

        string sibling = Path.Combine(baseDirectory[..at] + $"{Path.DirectorySeparatorChar}{CliFileName}{Path.DirectorySeparatorChar}" +
                                      baseDirectory[(at + marker.Length)..], executable);
        return File.Exists(sibling) ? sibling : null;
    }
}
