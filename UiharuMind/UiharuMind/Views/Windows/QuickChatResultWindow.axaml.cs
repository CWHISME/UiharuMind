using System;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils;
using UiharuMind.ViewModels.UIHolder;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class QuickChatResultWindow : QuickWindowBase
{
    public QuickChatResultWindow()
    {
        InitializeComponent();
        this.SetSimpledecorationWindow();
        // SizeToContent = SizeToContent.WidthAndHeight;
        
        _autoScrollHolder = new ScrollViewerAutoScrollHolder(ScrollViewer);
    }

    private ScrollViewerAutoScrollHolder _autoScrollHolder; 

    public void SeRequestInfo()
    {
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Test();
    }

    protected override void OnPreShow()
    {
        this.SetWindowToMousePosition(HorizontalAlignment.Center);
    }

    // private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    // {
    //     var scrollViewer = e.Source as ScrollViewer;
    //     if (scrollViewer == null) return;
    //     if (e.OffsetDelta.Y > 0)
    //     {
    //         // 用户向下滚动
    //         _isAutoScrolling = false;
    //     }
    //     else if (e.OffsetDelta.Y < 0)
    //     {
    //         // 用户向上滚动
    //         _isAutoScrolling = false;
    //     }
    //     else if (scrollViewer.Offset.Y>= scrollViewer.ScrollBarMaximum.Y-e.ExtentDelta.Y )
    //     {
    //         // 用户手动滚动到底部，恢复自动滚动
    //         _isAutoScrolling = true;
    //     }
    //     //Log.Debug($"scrollViewer.ScrollBarMaximum.Y: {scrollViewer.ScrollBarMaximum.Y}, scrollViewer.Offset.Y: {scrollViewer.Offset.Y}, e.ExtentDelta.Y: {e.ExtentDelta.Y}");
    //
    //     // 如果需要自动滚动到底部
    //     if (_isAutoScrolling)
    //     {
    //         scrollViewer.ScrollToEnd();
    //     }
    // }

    // protected override void OnPointerPressed(PointerPressedEventArgs e)
    // {
    //     if (e.ClickCount == 2)
    //     {
    //         SafeClose();
    //     }
    // }
    private void AddContent(string str)
    {
        ResultTextBlock.Text = str;
        // if (_isAutoScrolling)
        // {
        //     ScrollViewer.ScrollToEnd();
        // }
    }

    private StringBuilder sb = new StringBuilder();
    private Random random = new Random();

    private async void Test()
    {
        await RadomCharGenerator();
    }

    private async Task RadomCharGenerator()
    {
        while (IsVisible)
        {
            await Task.Delay(10);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 随机生成一个字符（假设为ASCII字符）
                char randomChar = (char)random.Next(32, 127);
                sb.Append(randomChar);
                AddContent(sb.ToString());
            });
        }
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }
}