using UiharuMind.Core.AI.Runtime;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Utils;

/// <summary>
/// 加载风险档位的显示文案
/// </summary>
public static class RuntimeRiskLabels
{
    /// <summary>
    /// 档位文案
    /// </summary>
    /// <param name="level">风险档位</param>
    /// <returns>低 / 警告 / 危险 / 未知</returns>
    public static string Format(RuntimeLoadRiskLevel level) => level switch
    {
        RuntimeLoadRiskLevel.Danger => Loc.Text(LangKey.ModelRuntimeRiskDanger),
        RuntimeLoadRiskLevel.Warning => Loc.Text(LangKey.ModelRuntimeRiskWarning),
        RuntimeLoadRiskLevel.Unknown => Loc.Text(LangKey.ModelRuntimeRiskUnknown),
        _ => Loc.Text(LangKey.ModelRuntimeRiskLow)
    };
}
