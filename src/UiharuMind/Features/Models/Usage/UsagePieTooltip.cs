using System;
using System.Collections.Generic;
using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView.SKCharts;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 饼图的悬停提示。<b>钉在图表右上角</b>，不跟指针走。
///
/// <para>
/// 库里饼图的提示位置是写死的「悬停点正上方」（<c>GetTooltipLocation</c> 对非笛卡尔图表
/// 根本不看 <c>TooltipPosition</c>），而这个饼只有 180px 高，提示正好压在扇区和鼠标上，
/// 指针一动还跟着抖。位置固定后指针始终看得见。
/// </para>
/// <para>
/// 顺带关掉楔形（那个指向扇区的小三角）：位置固定了它指不出任何东西，只会戳在角落里。
/// </para>
/// </summary>
internal sealed class UsagePieTooltip : SKDefaultTooltip
{
    private const float Edge = 4;

    public UsagePieTooltip()
    {
        Wedge = 0;
    }

    /// <inheritdoc />
    public override void Show(IEnumerable<ChartPoint> foundPoints, Chart chart)
    {
        base.Show(foundPoints, chart);

        LvcSize size = Measure();
        X = Math.Max(Edge, chart.ControlSize.Width - size.Width - Edge);
        Y = Edge;
    }
}
