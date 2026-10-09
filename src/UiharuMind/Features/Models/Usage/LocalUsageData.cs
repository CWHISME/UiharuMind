using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.Process;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 占用面板里的一行：一个在跑的本地模型进程
/// </summary>
public sealed class LocalUsageRow(LocalEngineEntry entry, EngineUsage? usage, Action afterStop)
{
    /// <summary>
    /// 种类
    /// </summary>
    public ELocalEngineKind Kind => entry.Kind;

    /// <summary>
    /// 名字
    /// </summary>
    public string Name => entry.Name;

    /// <summary>
    /// 正在加载
    /// </summary>
    public bool IsLoading => entry.LoadProgress != null;

    /// <summary>
    /// 实际占用；加载中或读不到为 null
    /// </summary>
    public EngineUsage? Usage => usage;

    /// <summary>
    /// 状态：加载到几成，或占用多少
    /// </summary>
    public string StateText => entry.LoadProgress is { } progress
        ? Loc.Text(LangKey.LocalUsageLoading, (int)(progress * 100))
        : usage != null
            ? UsageText(usage)
            : Loc.Text(LangKey.LocalUsageRunning);

    /// <summary>
    /// 停掉，内存还回去
    /// </summary>
    public IRelayCommand StopCommand { get; } = new RelayCommand(() =>
    {
        entry.Release();
        afterStop();
    });

    /// <summary>
    /// 「占用 5.2 GB · CPU 3%」，独立显卡上还有「显存 x GB」
    /// </summary>
    /// <param name="usage">占用</param>
    /// <returns>文案</returns>
    public static string UsageText(EngineUsage usage)
    {
        string memory = LocalUsageData.Gb(usage.MemoryBytes);
        int? cpu = usage.CpuPercent is { } value ? (int)Math.Round(value) : null;
        return (usage.GpuMemoryBytes is { } gpuBytes ? LocalUsageData.Gb(gpuBytes) : null, cpu) switch
        {
            ({ } gpu, { } percent) => Loc.Text(LangKey.EngineStatusUsageGpu, memory, gpu, percent),
            ({ } gpu, null) => Loc.Text(LangKey.EngineStatusMemoryGpu, memory, gpu),
            (null, { } percent) => Loc.Text(LangKey.EngineStatusUsage, memory, percent),
            _ => Loc.Text(LangKey.EngineStatusMemory, memory)
        };
    }
}

/// <summary>
/// 本机资源一览：整机可用内存与 CPU，加上每个在跑的本地模型进程的占用、一键停。
/// 模型页右上角与对话页模型栏共用一份（<see cref="Shared"/>），由看得见的那个控件定时 <see cref="RefreshAsync"/>；
/// 读进程与系统在后台线程，结果回界面线程写
/// </summary>
public partial class LocalUsageData : ObservableObject
{
    private static readonly Lazy<LocalUsageData> SharedInstance = new(() =>
    {
        SystemCpuMeter cpu = new();
        return new LocalUsageData(LocalEngines.Current, () => RuntimeDeviceInfoProvider.Capture(), cpu.Sample,
            SystemMemoryStatusReader.Read);
    });

    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1); //两处同时看得见时别各刷一遍

    private readonly Func<IReadOnlyList<LocalEngineEntry>> _entries;
    private readonly Func<RuntimeDeviceInfo> _device;
    private readonly Func<double?> _cpu;
    private readonly Func<SystemMemoryStatus> _memoryStatus;
    private Task? _refreshing; //正在取的那次，再来的等它
    private long _lastRefresh = long.MinValue / 2;

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _memoryLine = "";
    [ObservableProperty] private string _cpuLine = "";
    [ObservableProperty] private string _gpuLine = "";
    [ObservableProperty] private double _memoryUsedRatio;
    [ObservableProperty] private string _swapLine = "";
    [ObservableProperty] private ESystemMemoryPressure _pressure;
    [ObservableProperty] private string _pressureTag = "Ready";
    [ObservableProperty] private bool _isMemoryLow;
    [ObservableProperty] private IReadOnlyList<LocalUsageRow> _rows = [];
    [ObservableProperty] private bool _hasRows;

    /// <param name="entries">在跑的本地模型进程（界面线程上取）</param>
    /// <param name="device">本机内存（后台线程上取）</param>
    /// <param name="cpu">整机 CPU（后台线程上取）</param>
    /// <param name="memoryStatus">系统内存压力、压缩与交换区（后台线程上取）；不给为读不到</param>
    public LocalUsageData(Func<IReadOnlyList<LocalEngineEntry>> entries, Func<RuntimeDeviceInfo> device,
        Func<double?> cpu, Func<SystemMemoryStatus>? memoryStatus = null)
    {
        _entries = entries;
        _device = device;
        _cpu = cpu;
        _memoryStatus = memoryStatus ?? (() => SystemMemoryStatus.Empty);
    }

    /// <summary>
    /// 全应用共用的一份
    /// </summary>
    public static LocalUsageData Shared => SharedInstance.Value;

    /// <summary>
    /// 重取一遍（界面线程上调）；正在取就等那次，刚取过就跳过
    /// </summary>
    /// <param name="force">不管刚取过没有</param>
    public Task RefreshAsync(bool force = false)
    {
        if (_refreshing is { IsCompleted: false } running) return running;
        if (!force && Environment.TickCount64 - _lastRefresh < MinInterval.TotalMilliseconds) return Task.CompletedTask;
        return _refreshing = RefreshCoreAsync();
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            IReadOnlyList<LocalEngineEntry> entries = _entries();
            (RuntimeDeviceInfo device, double? cpu, SystemMemoryStatus status, EngineUsage?[] usages) =
                await Task.Run(() => (
                    _device(),
                    _cpu(),
                    _memoryStatus(),
                    entries.Select(x => x.LoadProgress == null ? x.SampleUsage() : null).ToArray()));
            Apply(device, cpu, status, entries, usages);
        }
        finally
        {
            _lastRefresh = Environment.TickCount64;
        }
    }

    /// <summary>
    /// 某个对话模型那一行
    /// </summary>
    /// <param name="modelName">模型名</param>
    /// <returns>没在跑为 null</returns>
    public LocalUsageRow? RowOf(string? modelName) =>
        modelName == null ? null : Rows.FirstOrDefault(x => x.Kind == ELocalEngineKind.Chat && x.Name == modelName);

    /// <summary>
    /// 系统不给压力档时（Windows、Linux）按可用占总量分档：低于两成警告，低于一成严重
    /// </summary>
    /// <param name="available">可用（字节）</param>
    /// <param name="total">总量（字节）</param>
    /// <returns>档位</returns>
    public static ESystemMemoryPressure Classify(long available, long total)
    {
        if (total <= 0) return ESystemMemoryPressure.Normal;
        double ratio = (double)available / total;
        return ratio < 0.1 ? ESystemMemoryPressure.Critical
            : ratio < 0.2 ? ESystemMemoryPressure.Warning
            : ESystemMemoryPressure.Normal;
    }

    internal static string Gb(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("0.0");

    private void Apply(RuntimeDeviceInfo device, double? cpu, SystemMemoryStatus status,
        IReadOnlyList<LocalEngineEntry> entries, EngineUsage?[] usages)
    {
        string available = Gb(device.AvailableMemoryBytes);
        string total = Gb(device.TotalMemoryBytes);
        int? cpuPercent = cpu is { } value ? (int)Math.Round(value) : null;
        Summary = !device.HasMemoryInfo
            ? cpuPercent is { } c ? Loc.Text(LangKey.LocalUsageCpuLine, c) : ""
            : cpuPercent is { } percent
                ? Loc.Text(LangKey.LocalUsageSummary, available, total, percent)
                : Loc.Text(LangKey.LocalUsageSummaryMemory, available, total);
        MemoryLine = device.HasMemoryInfo ? Loc.Text(LangKey.LocalUsageMemoryLine, available, total) : "";
        CpuLine = cpuPercent is { } line ? Loc.Text(LangKey.LocalUsageCpuLine, line) : "";
        GpuLine = device.HasDedicatedGpuMemory
            ? Loc.Text(LangKey.LocalUsageGpuLine, Gb(device.GpuAvailableMemoryBytes), Gb(device.GpuTotalMemoryBytes),
                device.GpuName)
            : "";
        MemoryUsedRatio = device.TotalMemoryBytes > 0
            ? 1 - (double)device.AvailableMemoryBytes / device.TotalMemoryBytes
            : 0;
        // 可用余量看不出已经在换页，系统给了压力档就以它为准（活动监视器「内存压力」那条线）
        Pressure = status.Pressure != ESystemMemoryPressure.Unknown ? status.Pressure
            : device.HasMemoryInfo ? Classify(device.AvailableMemoryBytes, device.TotalMemoryBytes)
            : ESystemMemoryPressure.Normal;
        PressureTag = Pressure switch
        {
            ESystemMemoryPressure.Critical => "Error",
            ESystemMemoryPressure.Warning => "Warning",
            _ => "Ready"
        };
        IsMemoryLow = Pressure is ESystemMemoryPressure.Warning or ESystemMemoryPressure.Critical;
        SwapLine = status switch
        {
            { CompressedBytes: > 0 } => Loc.Text(LangKey.LocalUsageSwapLine, Gb(status.CompressedBytes),
                Gb(status.SwapUsedBytes)),
            { SwapUsedBytes: > 0 } => Loc.Text(LangKey.LocalUsageSwapOnlyLine, Gb(status.SwapUsedBytes)),
            _ => ""
        };
        Rows = entries.Select((x, i) => new LocalUsageRow(x, usages[i], () => _ = RefreshAsync(true))).ToList();
        HasRows = Rows.Count > 0;
    }
}
