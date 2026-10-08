using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 下载前「这台机器跑不跑得动」的红黄绿点：圆点样式与悬停说明
/// </summary>
public static class RuntimeRiskBadge
{
    /// <summary>
    /// 风险档位对应的圆点样式（status-dot 的 Tag）
    /// </summary>
    /// <param name="level">风险档位</param>
    /// <returns>Ready / Warning / Error / Idle</returns>
    public static string Tag(RuntimeLoadRiskLevel level) => level switch
    {
        RuntimeLoadRiskLevel.Low => "Ready",
        RuntimeLoadRiskLevel.Warning => "Warning",
        RuntimeLoadRiskLevel.Danger => "Error",
        _ => "Idle"
    };

    /// <summary>
    /// 悬停说明：估算占用 / 总内存
    /// </summary>
    /// <param name="estimatedBytes">估算占用</param>
    /// <param name="device">设备信息</param>
    /// <returns>说明</returns>
    public static string Tip(long estimatedBytes, RuntimeDeviceInfo device) => device.HasMemoryInfo
        ? Loc.Text(LangKey.ModelDownloadRiskTip, GameUtils.FormatBytes(estimatedBytes),
            GameUtils.FormatBytes(device.TotalMemoryBytes))
        : Loc.Text(LangKey.ModelRuntimeRiskUnknown);
}
