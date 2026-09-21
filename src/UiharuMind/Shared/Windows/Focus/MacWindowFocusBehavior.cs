using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Windows.Focus;

/// <summary>
/// macOS：辅助窗口（快捷面板、浮窗、钉图）走 nonactivating panel 路线取焦点。
/// <para>
/// 要解决的是 AppKit 的两条「顺手抬窗」：一是应用被激活时它把本应用所有窗口整组抬到前台
/// （Avalonia 的 Show 里就带 activateIgnoringOtherApps:），二是 key 窗口消失时它改派 key，
/// 而 Avalonia 在 windowDidBecomeKey 里对新 key 窗口 orderFront。两条都会把后台的主界面翻上来。
/// </para>
/// <para>
/// 常规窗口不受影响，照旧走完整激活。
/// </para>
/// </summary>
public class MacWindowFocusBehavior : IWindowFocusBehavior
{
    private bool _isNonactivatingPanel; //已转成 nonactivating panel，取焦点时不必激活本应用
    private bool _levelRestoreHooked; //失焦降回普通层的订阅只挂一次

    public void PrepareShow(UiharuWindowBase window)
    {
        // Avalonia 的 Show 带 activateIgnoringOtherApps:，会整组抬窗。辅助窗口自己取焦点，这里先关掉
        if (window.IsAuxiliaryWindow) window.ShowActivated = false;
    }

    public void AfterShow(UiharuWindowBase window)
    {
        if (window.IsAuxiliaryWindow) _isNonactivatingPanel = MacPanelWindowService.TryMakeNonactivatingPanel(window);
    }

    public void Focus(UiharuWindowBase window)
    {
        if (!_isNonactivatingPanel)
        {
            WindowActivationService.Activate(window);
            return;
        }

        MacPanelWindowService.FocusPanel(window);

        // 常驻置顶的浮窗本来就在 floating 层，压得住前台应用；普通层级的压不住，另行处理
        if (window.Topmost) return;

        // 能激活就激活（菜单栏归本应用更自然），但只带自己上来，不整组抬窗。
        // 全局快捷键唤出时这一步多半会被系统的协作式激活拒掉——用户没碰过本应用，没有激活权
        MacPanelWindowService.ActivateAppForWindowOnly(window);
        LiftAboveForegroundApp(window);
    }

    public void PrepareClose(UiharuWindowBase window) => MacWindowFocusGuard.SuppressKeyHandoff(window);

    /// <summary>
    /// 临时把窗口抬到 floating 层压过前台应用。
    /// macOS 不让后台应用的普通层窗口排到前台应用之前（orderFrontRegardless 与自激活都试过，
    /// 全局快捷键场景下都不成立），只有换层级压得住。窗口一失焦就降回普通层，不让它常驻在别人头上。
    /// </summary>
    private void LiftAboveForegroundApp(UiharuWindowBase window)
    {
        OverlayWindowService.ApplyNativeWindowLevel(window, EOverlayWindowLevel.Floating);

        if (_levelRestoreHooked) return;
        _levelRestoreHooked = true;
        window.Deactivated += (_, _) =>
            OverlayWindowService.ApplyNativeWindowLevel(window, EOverlayWindowLevel.Normal);
    }
}
