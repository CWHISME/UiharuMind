using Avalonia;
using Avalonia.Media;
using System;

namespace UiharuMind.Features.QuickTools
{
    public static class PieSliceHelper
    {
        /// <summary>
        /// 创建一个扇形路径几何
        /// </summary>
        /// <param name="center">中心点</param>
        /// <param name="radius">外半径</param>
        /// <param name="innerRadius">内半径（中间留空，像甜甜圈）</param>
        /// <param name="startAngle">起始角度（度）</param>
        /// <param name="sweepAngle">跨越角度（度）</param>
        public static Geometry CreatePieSlice(Point center, double radius, double innerRadius, double startAngle, double sweepAngle)
        {
            var geometry = new StreamGeometry();
            
            using (var context = geometry.Open())
            {
                // 角度转弧度
                double startRad = startAngle * Math.PI / 180.0;
                double sweepRad = sweepAngle * Math.PI / 180.0;
                double endRad = startRad + sweepRad;

                // 计算外弧起止点
                Point outerStart = new Point(
                    center.X + radius * Math.Cos(startRad),
                    center.Y + radius * Math.Sin(startRad));
                
                Point outerEnd = new Point(
                    center.X + radius * Math.Cos(endRad),
                    center.Y + radius * Math.Sin(endRad));

                // 计算内弧起止点
                Point innerStart = new Point(
                    center.X + innerRadius * Math.Cos(startRad),
                    center.Y + innerRadius * Math.Sin(startRad));
                
                Point innerEnd = new Point(
                    center.X + innerRadius * Math.Cos(endRad),
                    center.Y + innerRadius * Math.Sin(endRad));

                // 开始绘制路径
                context.BeginFigure(outerStart, true);
                
                // 画外弧 (从 Start 到 End)
                context.ArcTo(
                    outerEnd,
                    new Size(radius, radius),
                    0, // 旋转角度
                    sweepAngle > 180, // 是否大弧
                    SweepDirection.Clockwise);

                // 画直线到内弧终点
                context.LineTo(innerEnd);

                // 画内弧 (从 End 到 Start，逆时针)
                if (innerRadius > 0)
                {
                    context.ArcTo(
                        innerStart,
                        new Size(innerRadius, innerRadius),
                        0,
                        sweepAngle > 180,
                        SweepDirection.CounterClockwise);
                }
                else
                {
                    context.LineTo(center); // 如果内径为0，回到中心
                }

                context.EndFigure(true); // 闭合图形
            }

            return geometry;
        }
    }
}
