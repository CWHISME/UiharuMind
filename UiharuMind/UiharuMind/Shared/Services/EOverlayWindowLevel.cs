namespace UiharuMind.Shared.Services;

/// <summary>
/// 越过菜单栏的窗口档位（macOS 的 NSWindow level）。
///
/// Avalonia 的 Topmost 只到 floating 层（3），盖不住菜单栏，也分不出「谁压谁」——
/// 凡是要越过菜单栏的窗口都从这里取值，<b>整个应用的置顶次序就是本枚举的大小次序</b>，
/// 不要再在各窗口里写裸数字。
///
/// 取值的上限被系统卡死了：Avalonia 的 macOS 弹出层（右键菜单、Flyout、ToolTip）一律是
/// NSPopUpMenuWindowLevel(101)，写死在 PopupImpl.mm 里改不了。<b>任何要弹菜单的窗口都必须留在 101 之下</b>，
/// 否则菜单会开在窗口背后，表现成「右键毫无反应」。所以常驻窗口挤在菜单栏(24)与弹出层(101)之间，
/// 只有不弹任何菜单的全屏遮罩才用得起 screenSaver(1000)。
/// </summary>
public enum EOverlayWindowLevel : long
{
    /// <summary>普通窗口，不置顶。= NSNormalWindowLevel</summary>
    Normal = 0,

    /// <summary>
    /// 普通层与菜单栏之间的浮动层。= NSFloatingWindowLevel，不越过菜单栏。
    /// 快捷键唤出的弹窗（翻译、文件搜索）临时借它压过前台应用——macOS 不让后台应用的普通层窗口
    /// 排到前台应用之前，而全局快捷键唤出时用户没碰过本应用，自激活会被协作式激活机制拒绝。
    /// 窗口一失焦就降回 <see cref="Normal"/>，不常驻。
    /// </summary>
    Floating = 3,

    /// <summary>钉在屏幕上的贴图窗。= NSStatusWindowLevel，压过菜单栏(24)与 Dock(20)</summary>
    Pinned = 25,

    /// <summary>跟着贴图走的停靠工具条：贴图为投影留的那圈留白会压到它头上，必须高一档</summary>
    PinnedDock = 26,

    /// <summary>复制后弹出的浮动快捷工具：转瞬即逝且要用户马上看到，必须压在贴图与工具条之上</summary>
    FloatingTool = 27,

    /// <summary>全屏截图选区遮罩：必须盖住一切，且自身不弹任何菜单，可以用到 NSScreenSaverWindowLevel</summary>
    FullscreenOverlay = 1000,
}
