using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.DevAutomation;

/// <summary>文件相关的开发步骤</summary>
internal static class FileDevCommands
{
    /// <summary>造出全部文件步骤</summary>
    /// <returns>步骤集合</returns>
    public static IReadOnlyList<IDevCommand> CreateAll() => [new FileOpenCommand(), new FileCloseAllCommand()];
}

/// <summary>
/// 像用户点开一样打开文件（<see cref="FileOpener"/>），量从点下到窗口画出第一帧、到界面空闲各要多久
/// </summary>
internal sealed class FileOpenCommand : IAsyncDevCommand
{
    public string Name => "file.open";

    public string Usage => "像用户点开一样打开文件并计时。path：绝对路径。结果带 openMs（读盘 + 建窗装载）、shownMs（窗口可见）、" +
                           "frameMs（画出第一帧）、idleMs（后台分帧任务跑完）、打开后的文本窗数，" +
                           "以及期间抛过的首次异常（挂调试器时它们是秒级卡顿的常见来源）";

    public async Task<JsonNode?> ExecuteAsync(JsonElement args)
    {
        string path = DevCommandRegistry.RequireString(args, "path");
        // 挂着调试器时每个首次异常都要通知调试器，几个就能把打开拖到秒级，这里一并报出来
        Dictionary<string, int> exceptions = new();
        EventHandler<FirstChanceExceptionEventArgs> onException = (_, e) =>
        {
            string key = $"{e.Exception.GetType().Name}: {e.Exception.Message}";
            lock (exceptions) exceptions[key] = exceptions.GetValueOrDefault(key) + 1;
        };
        AppDomain.CurrentDomain.FirstChanceException += onException;
        CountingTraceListener trace = new();
        Trace.Listeners.Add(trace);
        Stopwatch sw = Stopwatch.StartNew();
        await FileOpener.OpenAsync(path);
        long openMs = sw.ElapsedMilliseconds;

        // 复用的缓存窗是投递显示的，先等它真的可见再等一帧，否则量到的是隐藏窗的假帧
        TextFileWindow? window = null;
        for (int i = 0; i < 200 && window == null; i++)
        {
            window = UIManager.GetWindows<TextFileWindow>().OfType<TextFileWindow>().LastOrDefault(w => w.IsVisible);
            if (window == null) await Task.Delay(25);
        }

        long shownMs = sw.ElapsedMilliseconds;
        TaskCompletionSource frame = new();
        if (window != null) window.RequestAnimationFrame(_ => frame.TrySetResult());
        else frame.TrySetResult();
        await frame.Task;
        long frameMs = sw.ElapsedMilliseconds;

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        long idleMs = sw.ElapsedMilliseconds;
        AppDomain.CurrentDomain.FirstChanceException -= onException;
        Trace.Listeners.Remove(trace);
        int windows = UIManager.GetWindows<TextFileWindow>().Count(w => w.IsVisible);
        return new JsonObject
        {
            ["path"] = path, ["openMs"] = openMs, ["shownMs"] = shownMs, ["frameMs"] = frameMs, ["idleMs"] = idleMs,
            ["windows"] = windows, ["exceptions"] = DevJson.Counts(exceptions), ["traceLines"] = trace.Top(),
        };
    }
}

/// <summary>关掉所有可见的文本窗，与点关闭按钮同一条路（窗口进缓存待复用）</summary>
internal sealed class FileCloseAllCommand : IDevCommand
{
    public string Name => "file.closeall";

    public string Usage => "关掉所有可见的文本窗（与点关闭按钮一样，窗口进缓存待复用），无参数";

    public JsonNode? Execute(JsonElement args)
    {
        List<TextFileWindow> windows = UIManager.GetWindows<TextFileWindow>().OfType<TextFileWindow>().Where(w => w.IsVisible).ToList();
        foreach (TextFileWindow window in windows) window.Close();
        return new JsonObject { ["closed"] = windows.Count };
    }
}

/// <summary>按内容数打开期间写进 Trace 的行（Avalonia 的 LogToTrace 落在这里，挂调试器时每行都同步送 IDE）</summary>
internal sealed class CountingTraceListener : TraceListener
{
    private readonly Dictionary<string, int> _lines = new();
    private string _pending = string.Empty;

    public override void Write(string? message) => _pending += message;

    public override void WriteLine(string? message)
    {
        string key = (_pending + message);
        if (key.Length > 200) key = key[..200];
        _pending = string.Empty;
        lock (_lines) _lines[key] = _lines.GetValueOrDefault(key) + 1;
    }

    /// <summary>出现最多的前几种行与总数</summary>
    public JsonObject Top()
    {
        lock (_lines)
        {
            return new JsonObject
            {
                ["total"] = _lines.Values.Sum(),
                ["top"] = DevJson.Array(_lines.OrderByDescending(x => x.Value).Take(8), x => $"{x.Value}× {x.Key}"),
            };
        }
    }
}
