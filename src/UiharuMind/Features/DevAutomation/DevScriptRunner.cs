/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.Core.DevControl;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 开发脚本执行器：把一份 JSONL 脚本按顺序在界面上跑一遍，把每一步的结果写成报告。
///
/// 存在的理由：长会话的性能与内存问题只在「真的用了一阵」之后才显形，而那条路径
/// 既走不完无头测试（它要真的排版、真的装载），也没法靠模拟键鼠可靠复现
/// （焦点、坐标、时序全是猜）。这里给的是<b>意图级</b>的一步：「打开这个会话」，
/// 而不是「点这个坐标」。
///
/// 这是启动期一次性的那条路，中途插不进话；要边跑边插话，用开发控制通道（<see cref="DevControlHost"/>，ADR 0059）。
/// 两条路共用同一组步骤（<see cref="DevStepExecutor"/>）。
///
/// 用法：<c>UiharuMind.Desktop --dev-script path/to/script.jsonl [--dev-report path/to/report.json]</c>
///
/// 脚本一行一步：<c>{"op":"page.jump","args":{"page":"agent"}}</c>，
/// 另有两个伪步骤：<c>{"op":"wait","args":{"ms":500}}</c> 等一会儿，
/// <c>{"op":"quit"}</c> 跑完退出应用（不写则留着界面给人看）。
/// </summary>
public static class DevScriptRunner
{
    private const string ScriptArgument = "--dev-script";
    private const string ReportArgument = "--dev-report";

    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

    /// <summary>
    /// 命令行里带了脚本就跑一遍（即发即忘，不挡启动）
    /// </summary>
    /// <param name="args">应用命令行参数</param>
    public static void RunIfRequested(IReadOnlyList<string>? args)
    {
        if (ValueOf(args, ScriptArgument) is not { } scriptPath) return;

        string reportPath = ValueOf(args, ReportArgument) ?? Path.ChangeExtension(scriptPath, ".report.json");
        // 脚本要驱动的是主窗口,而 DEBUG 构建启动时刻意只起托盘(见 App.OnFrameworkInitializationCompleted)。
        // 每一步都在界面上,没有窗口这份脚本从第一步就没有意义,所以由执行器负责先把它叫出来
        App.DummyWindow.LaunchMainWindow();
        _ = RunAsync(scriptPath, reportPath);
    }

    /// <summary>
    /// 退出应用：与托盘菜单那个「退出」同一条路。刻意不用 lifetime.Shutdown()：
    /// 本应用的窗口关闭是<b>隐藏</b>(常驻托盘),Shutdown 关完窗口进程照样活着——实机验证过
    /// </summary>
    internal static void QuitApp()
    {
        Dispatcher.UIThread.Post(() =>
        {
            (Avalonia.Application.Current as App)?.Dispose();
            Process.GetCurrentProcess().Kill();
        });
    }

    private static string? ValueOf(IReadOnlyList<string>? args, string name)
    {
        if (args == null) return null;

        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
        }

        return null;
    }

    private static async Task RunAsync(string scriptPath, string reportPath)
    {
        List<object> report = new();
        DevStepExecutor executor = new();

        Log.Debug($"Dev script: running '{scriptPath}'.");
        bool quit = false;
        try
        {
            string[] lines = await File.ReadAllLinesAsync(scriptPath).ConfigureAwait(true);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

                (object entry, bool stop) = await RunStepAsync(i + 1, line, executor).ConfigureAwait(true);
                report.Add(entry);
                await WriteReportAsync(reportPath, report).ConfigureAwait(true); //逐步落盘：长脚本（跑真模型）中途就能看进度
                if (stop)
                {
                    quit = true;
                    break;
                }
            }
        }
        catch (Exception e)
        {
            report.Add(new { step = -1, op = "<script>", ok = false, error = $"{e.GetType().Name}: {e.Message}" });
        }

        await WriteReportAsync(reportPath, report).ConfigureAwait(true);
        if (quit) QuitApp();
    }

    /// <param name="step">行号（从 1 起，便于对着脚本看）</param>
    /// <param name="line">这一行 JSON</param>
    /// <param name="executor">执行器</param>
    /// <returns>报告条目，以及要不要就此停下</returns>
    private static async Task<(object Entry, bool Stop)> RunStepAsync(int step, string line, DevStepExecutor executor)
    {
        string op = "<unparsed>";
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            op = document.RootElement.GetProperty("op").GetString() ?? string.Empty;
            if (op == "quit") return (new { step, op, ok = true, elapsedMs = 0 }, true);

            JsonElement args = document.RootElement.TryGetProperty("args", out JsonElement value) ? value : default;
            DevStepOutcome outcome = await executor.RunAsync(op, args).ConfigureAwait(true);
            // 一步失败不中止:后面的步骤往往还能说明问题,而报告里那条 error 已经把现场记下了
            return outcome.Ok
                ? (new { step, op, ok = true, elapsedMs = outcome.ElapsedMs, result = outcome.Result }, false)
                : (new { step, op, ok = false, elapsedMs = outcome.ElapsedMs, error = outcome.Error }, false);
        }
        catch (Exception e)
        {
            return (new { step, op, ok = false, elapsedMs = 0L, error = $"{e.GetType().Name}: {e.Message}" }, false);
        }
    }

    private static async Task WriteReportAsync(string reportPath, List<object> report)
    {
        try
        {
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, ReportOptions))
                .ConfigureAwait(true);
            Log.Debug($"Dev script: report written to '{reportPath}' ({report.Count} steps).");
        }
        catch (Exception e)
        {
            Log.Warning($"Dev script: writing report failed: {e.Message}");
        }
    }
}
