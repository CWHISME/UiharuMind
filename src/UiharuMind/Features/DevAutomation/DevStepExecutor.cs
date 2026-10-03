using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.Core.DevControl;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 执行开发步骤的一步：开发脚本（<see cref="DevScriptRunner"/>）与控制通道（<see cref="DevControlHost"/>）共用。
/// 步骤搬到界面线程上跑、计时、把结果或错误收成 <see cref="DevStepOutcome"/>；
/// 另认一个伪步骤 <c>wait</c>（<c>{"ms":500}</c>）
/// </summary>
internal sealed class DevStepExecutor
{
    private static readonly TimeSpan StepSettleDelay = TimeSpan.FromMilliseconds(250); //每步最小间隔:界面上很多事排在下一拍(装载、裁剪、补屏),当拍读到的是半成品

    private readonly Dictionary<string, IDevCommand> _commands =
        DevCommandRegistry.CreateAll().ToDictionary(x => x.Name, StringComparer.Ordinal);

    private const string WaitUsage = "等一会儿再走下一步。ms（默认 500）";

    /// <summary>全部步骤名（含 <c>wait</c>），按字母序</summary>
    public IReadOnlyList<string> Ops => Usages.Select(x => x.Op).ToList();

    /// <summary>全部步骤的用法（含 <c>wait</c>），按字母序</summary>
    public IReadOnlyList<DevStepUsage> Usages => _commands.Values
        .Select(x => new DevStepUsage(x.Name, x.Usage))
        .Append(new DevStepUsage("wait", WaitUsage))
        .OrderBy(x => x.Op, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// 执行一步。失败不抛，收进结果里
    /// </summary>
    /// <param name="op">步骤名</param>
    /// <param name="args">参数；没带时为 <c>undefined</c></param>
    /// <returns>结果</returns>
    public async Task<DevStepOutcome> RunAsync(string op, JsonElement args)
    {
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            if (op == "wait")
            {
                int ms = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("ms", out JsonElement w)
                    ? w.GetInt32()
                    : 500;
                await Task.Delay(ms).ConfigureAwait(true);
                return new DevStepOutcome(true, watch.ElapsedMilliseconds);
            }

            if (!_commands.TryGetValue(op, out IDevCommand? command))
            {
                throw new ArgumentException($"unknown op '{op}'; known: {string.Join(", ", Ops)}");
            }

            // 界面的活归界面线程。调用方在后台线程上推进,不搬过去就会在第一处属性赋值上炸
            object? result = command is IAsyncDevCommand asyncCommand
                ? await Dispatcher.UIThread.InvokeAsync(() => asyncCommand.ExecuteAsync(args))
                : await UiDispatcher.InvokeAsync(() => command.Execute(args));

            // 先读表再落位:落位等待是这里自己加的,算进耗时里每一步都是 250ms 打底,
            // 报告就再也看不出「切一个长会话到底花了多久」
            long elapsedMs = watch.ElapsedMilliseconds;
            await Task.Delay(StepSettleDelay).ConfigureAwait(true);
            return new DevStepOutcome(true, elapsedMs, result);
        }
        catch (Exception e)
        {
            return new DevStepOutcome(false, watch.ElapsedMilliseconds, Error: $"{e.GetType().Name}: {e.Message}");
        }
    }
}
