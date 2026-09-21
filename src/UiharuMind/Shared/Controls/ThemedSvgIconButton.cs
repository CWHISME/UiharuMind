using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 可点击的 Icon，无其它表现
/// </summary>
public class ThemedSvgIconButton : Button
{
    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<ThemedSvgIconButton, string>(nameof(IconName));

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public ThemedSvgIconButton()
    {
        //基础交互属性
        Background = Brushes.Transparent;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(4);
        Cursor = new Cursor(StandardCursorType.Hand);

        //直接给一个“无状态模板”
        Template = CreateTemplate();
    }

    private static IControlTemplate CreateTemplate()
    {
        return new FuncControlTemplate<ThemedSvgIconButton>((parent, _) =>
        {
            var icon = new ThemedSvgIcon
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            // A12 的 TemplateBinding 语法
            icon.Bind(
                ThemedSvgIcon.IconNameProperty,
                parent[!IconNameProperty]);

            return icon;
        });
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 禁用状态透明度（可选）
        if (change.Property == IsEnabledProperty)
        {
            Opacity = IsEnabled ? 1.0 : 0.5;
        }
    }
}