using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UiharuMind.Views.Common;

public abstract class UiharuWindowBase : Window
{
    public void RequestShow()
    {
        OnPreShow();
        Show();
    }

    protected virtual void OnPreShow()
    {
    }

    protected virtual void OnPreClose()
    {
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = true;
        SafeClose();
    }

    // public override void Hide()
    // {
    //     base.Hide();
    // }

    protected virtual void SafeClose()
    {
        OnPreClose();
        InvalidateMeasure();
        Task.Run(() =>
        {
            Task.Delay(100);
            Dispatcher.UIThread.InvokeAsync(Hide);
        });
    }
}