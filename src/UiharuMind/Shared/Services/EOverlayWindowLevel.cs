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

    /// <summary>快捷问答结果窗：压过普通浮窗，但贴图系、轮盘与复制工具都要盖住它</summary>
    QuickChatResult = 28,

    /// <summary>钉在屏幕上的贴图窗：比较高的档位，只低于轮盘与复制工具。= NSStatusWindowLevel，压过菜单栏(24)与 Dock(20)</summary>
    Pinned = 29,

    /// <summary>跟着贴图走的停靠工具条：贴图为投影留的那圈留白会压到它头上，必须始终比贴图高一档</summary>
    PinnedDock = 30,

    /// <summary>快捷轮盘：应用内第二高（仅次于复制工具）；也要能越过菜单栏。轮盘项带 ToolTip，必须留在弹出层(101)之下</summary>
    RadialMenu = 31,

    /// <summary>复制后弹出的浮动快捷工具：应用内置顶的最高档(全屏遮罩除外)；转瞬即逝且要用户马上看到，必须压在一切之上</summary>
    FloatingTool = 32,

    /// <summary>全屏截图选区遮罩：必须盖住一切，且自身不弹任何菜单，可以用到 NSScreenSaverWindowLevel</summary>
    FullscreenOverlay = 1000,
}
