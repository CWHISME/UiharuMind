using System.Diagnostics;
using UiharuMind.Core.AI.Runtime;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 设备信息采集的开销上限。切换一次模型会经 PropertyChanged 风暴触发十余次
/// RefreshStatus，每次都走 Capture——它在 macOS 上要跑 4 个外部进程
/// (sysctl×2 / vm_stat / sysctl cpu)，无缓存时整轮切换把 UI 线程卡住近一秒。
/// </summary>
public class DeviceInfoCaptureCostTests
{
    private const int SwitchBurstCount = 15; //一次模型切换的实测量级
    private const int BudgetMs = 100;

    [Fact]
    public void CaptureBurst_StaysWithinBudget()
    {
        RuntimeDeviceInfoProvider.Capture(); //预热,不计首次进程启动

        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < SwitchBurstCount; i++) RuntimeDeviceInfoProvider.Capture();
        long elapsed = sw.ElapsedMilliseconds;

        Assert.True(elapsed < BudgetMs,
            $"{SwitchBurstCount} 次 Capture 耗时 {elapsed}ms,超出预算 {BudgetMs}ms——切换模型会卡住 UI 线程");
    }
}
