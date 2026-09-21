using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using UiharuMind.Views.Common;
using UiharuMind.Features.Conversation.QuickChat;

namespace UiharuMind.Views.Windows;

public partial class QuickWheelWindow : QuickWindowBase
{
    private readonly List<WheelItem> _items = new();
    private int _hoveredIndex = -1;
    private const double ItemRadius = 65.0;
    private const double ItemSize = 40.0;

    public static bool HasSelection { get; private set; }

    public QuickWheelWindow()
    {
        InitializeComponent();

        // Initialize wheel items
        _items.Add(new WheelItem("File Search", "🔍", () => QuickFileSearchWindow.Show()));
        // Future items can be added here

        HasSelection = false;
    }

    public static void Show()
    {
        HasSelection = false;
        UIManager.ShowWindow<QuickWheelWindow>(x => x.BuildWheel());
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        Position = GetCenterPosition();
    }

    private PixelPoint GetCenterPosition()
    {
        var screens = Screens?.All;
        if (screens == null || screens.Count == 0)
            return new PixelPoint(500, 500);

        var screen = screens[0];
        var bounds = screen.Bounds;
        int x = bounds.X + (bounds.Width - (int)Width) / 2;
        int y = bounds.Y + (bounds.Height - (int)Height) / 2;
        return new PixelPoint(x, y);
    }

    private void BuildWheel()
    {
        WheelCanvas.Children.Clear();

        double centerX = 90;
        double centerY = 90;
        double angleStep = 360.0 / _items.Count;

        for (int i = 0; i < _items.Count; i++)
        {
            double angle = angleStep * i - 90; // Start from top
            double rad = angle * Math.PI / 180;

            double x = centerX + ItemRadius * Math.Cos(rad) - ItemSize / 2;
            double y = centerY + ItemRadius * Math.Sin(rad) - ItemSize / 2;

            var button = new Button
            {
                Width = ItemSize,
                Height = ItemSize,
                Content = _items[i].Icon,
                Tag = i,
                Classes = { "WheelItem" }
            };

            int index = i;
            button.PointerEntered += (s, e) => _hoveredIndex = index;
            button.PointerExited += (s, e) => _hoveredIndex = -1;
            button.Click += OnItemClick;

            Canvas.SetLeft(button, x);
            Canvas.SetTop(button, y);
            WheelCanvas.Children.Add(button);
        }
    }

    private void OnItemClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int index)
        {
            HasSelection = true;
            _items[index].Action?.Invoke();
            CloseByAnimation();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        // If Alt released without selection, will be handled by DummyWindow
    }

    public static void ExecuteDefaultAction()
    {
        QuickStartChatWindow.Show("");
        UIManager.CloseWindow<QuickWheelWindow>();
    }

    private class WheelItem
    {
        public string Name { get; }
        public string Icon { get; }
        public Action? Action { get; }

        public WheelItem(string name, string icon, Action? action)
        {
            Name = name;
            Icon = icon;
            Action = action;
        }
    }
}
