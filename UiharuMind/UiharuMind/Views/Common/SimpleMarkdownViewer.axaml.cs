using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.UIHolder;

namespace UiharuMind.Views.Common;

public partial class SimpleMarkdownViewer : UserControl
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<SimpleMarkdownViewer, string>(nameof(Markdown));

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set
        {
            // Log.Debug(_stopwatch.ElapsedMilliseconds + "       Setting Markdown to: ");
            SetValue(MarkdownProperty, value);
            // _stopwatch.Restart();
        }
    }

    private ScrollViewerAutoScrollHolder _scrollViewerAutoScrollHolder;
    // private Stopwatch _stopwatch = new Stopwatch();

    public SimpleMarkdownViewer()
    {
        InitializeComponent();

        // _stopwatch.Start();
        _scrollViewerAutoScrollHolder =
            new ScrollViewerAutoScrollHolder((ScrollViewer)this.LogicalChildren[0].LogicalChildren[0]);
    }
}