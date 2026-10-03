using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DevControl;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 开发控制通道的开关与接线（ADR 0059）：开发者模式开着、或带 <c>--dev-control</c> 启动时开通道；
/// 运行中开 / 关开发者模式，通道跟着开 / 关（带了参数的一直开着）。
/// 步骤交给 <see cref="DevStepExecutor"/>，与开发脚本同一组；另答 <c>app.ops</c> 与 <c>app.quit</c>
/// </summary>
public sealed class DevControlHost : IDisposable
{
    private const string ControlArgument = "--dev-control";
    private static readonly TimeSpan QuitDelay = TimeSpan.FromMilliseconds(200); //先让 app.quit 的回复写出去

    private readonly bool _forced;
    private readonly DevStepExecutor _executor = new();
    private DevControlServer? _server;
    private int _windowLaunched; //0 / 1，并发的头几步只叫一次

    private DevControlHost(bool forced)
    {
        _forced = forced;
    }

    /// <summary>
    /// 按开发者模式与启动参数开通道，并跟着开发者模式开关
    /// </summary>
    /// <param name="args">应用命令行参数</param>
    /// <returns>宿主，退出时释放</returns>
    public static DevControlHost Start(IReadOnlyList<string>? args)
    {
        DevControlHost host = new(args?.Contains(ControlArgument, StringComparer.Ordinal) == true);
        DeveloperMode.Changed += host.Refresh;
        host.Refresh();
        return host;
    }

    public void Dispose()
    {
        DeveloperMode.Changed -= Refresh;
        Close();
    }

    private void Refresh()
    {
        if (_forced || DeveloperMode.IsEnabled) Open();
        else Close();
    }

    private void Open()
    {
        if (_server != null) return;

        DevControlServer server = new(DevControlEndpoint.Default, HandleAsync);
        try
        {
            if (server.Start()) _server = server;
        }
        catch (Exception e)
        {
            // 开不了通道不该拖垮启动或解锁：记下来，应用照常用
            server.Dispose();
            Log.Warning($"Dev control: cannot open at '{DevControlEndpoint.Default.SocketPath}': {e.GetType().Name}: {e.Message}");
        }
    }

    private void Close()
    {
        _server?.Dispose();
        _server = null;
    }

    private async Task<DevStepOutcome> HandleAsync(string op, JsonElement args)
    {
        switch (op)
        {
            case "app.ops":
                return new DevStepOutcome(true, 0, _executor.Ops);
            case "app.quit":
                _ = Task.Delay(QuitDelay).ContinueWith(_ => DevScriptRunner.QuitApp(), TaskScheduler.Default);
                return new DevStepOutcome(true, 0);
        }

        // 步骤驱动的是主窗口，DEBUG 构建启动时只起托盘；第一步之前叫出来一次，之后不再抢焦点
        if (Interlocked.Exchange(ref _windowLaunched, 1) == 0)
            await Dispatcher.UIThread.InvokeAsync(() => App.DummyWindow.LaunchMainWindow());

        return await _executor.RunAsync(op, args).ConfigureAwait(false);
    }
}
