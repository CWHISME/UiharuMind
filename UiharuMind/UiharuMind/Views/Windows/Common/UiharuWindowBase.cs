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

    public virtual void Awake()
    {
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

    protected virtual void SafeClose()
    {
        OnPreClose();
        Dispatcher.UIThread.Post(Hide);
    }
}