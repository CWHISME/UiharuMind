using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Windows.Focus;

/// <summary>
/// Windows / Linux：窗口取焦点就是正常激活，没有额外讲究。
/// </summary>
public class DefaultWindowFocusBehavior : IWindowFocusBehavior
{
    public void PrepareShow(UiharuWindowBase window)
    {
    }

    public void AfterShow(UiharuWindowBase window)
    {
    }

    public void Focus(UiharuWindowBase window) => WindowActivationService.Activate(window);

    public void PrepareClose(UiharuWindowBase window)
    {
    }
}
