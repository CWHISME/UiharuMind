using System;
using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Controls
{
    public class RadialMenu : TemplatedControl
    {
        public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
            AvaloniaProperty.Register<RadialMenu, IEnumerable>(nameof(ItemsSource));
        public IEnumerable ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

        public static readonly StyledProperty<double> RadiusProperty =
            AvaloniaProperty.Register<RadialMenu, double>(nameof(Radius), 100.0);
        public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

        public static readonly StyledProperty<double> InnerRadiusProperty =
            AvaloniaProperty.Register<RadialMenu, double>(nameof(InnerRadius), 30.0);
        public double InnerRadius { get => GetValue(InnerRadiusProperty); set => SetValue(InnerRadiusProperty, value); }

        private Panel? _container;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _container = e.NameScope.Find<Panel>("PART_Container");
            GenerateItems();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ItemsSourceProperty)
            {
                GenerateItems();
            }
        }

        public RadialMenuItem? GetHoveredItem()
        {
            if (_container == null) return null;
            foreach (var child in _container.Children)
            {
                if (child is RadialMenuItem item && item.IsHovered)
                    return item;
            }
            return null;
        }

        private void GenerateItems()
        {
            if (_container == null || ItemsSource == null) return;
            _container.Children.Clear();

            int count = 0;
            foreach (var item in ItemsSource) count++;
            if (count == 0) return;

            double angleStep = 360.0 / count;
            double currentAngle = -90;

            foreach (var item in ItemsSource)
            {
                var menuItem = new RadialMenuItem();

                menuItem.Bind(RadialMenuItem.IconProperty, new Binding("Icon"));
                menuItem.Bind(RadialMenuItem.TextProperty, new Binding("Text"));
                menuItem.Bind(RadialMenuItem.CommandProperty, new Binding("ActionCommand"));
                menuItem.DataContext = item;

                var geometry = PieSliceHelper.CreatePieSlice(
                    new Point(Radius, Radius),
                    Radius,
                    InnerRadius,
                    currentAngle,
                    angleStep
                );
                menuItem.SliceGeometry = geometry;

                double midAngleRad = (currentAngle + angleStep / 2) * Math.PI / 180.0;
                double midRadius = (Radius + InnerRadius) / 2;

                double iconX = Radius + midRadius * Math.Cos(midAngleRad);
                double iconY = Radius + midRadius * Math.Sin(midAngleRad);

                menuItem.IconPosition = new Point(iconX, iconY);

                menuItem.Width = Radius * 2;
                menuItem.Height = Radius * 2;

                _container.Children.Add(menuItem);

                currentAngle += angleStep;
            }
        }
    }
}
