// RadialPanel.cs
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace UiharuMind.Features.QuickTools
{
    public class RadialPanel : Panel
    {
        public static readonly StyledProperty<double> RadiusProperty =
            AvaloniaProperty.Register<RadialPanel, double>(nameof(Radius), defaultValue: 80.0);

        public double Radius
        {
            get => GetValue(RadiusProperty);
            set => SetValue(RadiusProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // 1. 测量所有子元素
            foreach (var child in Children)
            {
                child.Measure(availableSize);
            }

            // 2. 计算面板所需大小（直径 + 最大子元素尺寸）
            var maxChildSize = Children.Count > 0 
                ? new Size(Children.Max(c => c.DesiredSize.Width), Children.Max(c => c.DesiredSize.Height))
                : new Size(0, 0);

            // 确保面板至少有直径大小
            var desiredSize = new Size(
                Math.Max(Radius * 2, Radius * 2 + maxChildSize.Width),
                Math.Max(Radius * 2, Radius * 2 + maxChildSize.Height)
            );

            return desiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // 3. 安排子元素在圆周上的位置
            var center = new Point(finalSize.Width / 2, finalSize.Height / 2);
            var childCount = Children.Count;

            if (childCount == 0)
                return finalSize;

            var angleStep = 2 * Math.PI / childCount;

            for (int i = 0; i < childCount; i++)
            {
                var child = Children[i];
                // 从顶部开始（-90度）
                var angle = i * angleStep - Math.PI / 2;

                var x = center.X + Radius * Math.Cos(angle) - child.DesiredSize.Width / 2;
                var y = center.Y + Radius * Math.Sin(angle) - child.DesiredSize.Height / 2;

                // 确保 child 有有效尺寸，否则使用默认尺寸
                var childSize = child.DesiredSize.Width > 0 && child.DesiredSize.Height > 0 
                    ? child.DesiredSize 
                    : new Size(60, 60); // 默认尺寸与 XAML 中一致

                child.Arrange(new Rect(new Point(x, y), childSize));
            }

            return finalSize;
        }
    }
}
