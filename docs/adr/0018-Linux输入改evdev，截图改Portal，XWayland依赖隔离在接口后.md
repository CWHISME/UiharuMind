# Linux 输入改 evdev，截图改 Portal，XWayland 依赖隔离在接口后

Linux/Wayland 下快捷键、截图、鼠标位置三样全废。输入监听与模拟从 SharpHook 换成
**evdev/uinput**，截图从桌面环境 CLI 换成 **xdg-desktop-portal**，
而「查询全局光标位置」这一处无法回避的 XWayland 依赖收敛到 `IPointerLocator` 之后。
Avalonia 升到 12.1.1，Linux 上**显式钉死 X11 后端**。

目标环境：Ubuntu 26.04 / GNOME 49+ / Wayland 独占。

## 三个症状，两个根因

| 症状 | 根因 |
|---|---|
| 快捷键失效 | SharpHook(libuiohook) 在 Linux 只有 X11 的 XRecord 实现，官方口径「Only X11 is supported」。Wayland 下拿不到全局事件 |
| 鼠标位置失效 | 不是独立缺陷。`ScreensService.MousePosition` 读的就是钩子事件里的坐标，随钩子一起死 |
| 截图失效 | 双重死亡：① Wayland 不允许客户端抓屏；② GNOME 49 把 `org.gnome.Shell.Screenshot` 收为私有 API，连 `gnome-screenshot` 本身都已失效，而旧代码的 GNOME 分支正是调它 |

## 决策

1. **输入走 evdev/uinput，不走 GNOME Shell 扩展。** 读 `/dev/input/event*` 监听、写 `/dev/uinput`
   模拟，完全绕过显示服务器。扩展方案能拿到无特权的系统级快捷键和精确光标位置，但只对 GNOME
   有效、随 GNOME 大版本可能整个失效，且拦不住按键、做不了录制回放。

2. **不做 `EVIOCGRAB`，即不吞键。** 吞键要求独占设备后把未消费的按键用 uinput 逐个回注，
   等于把本应用变成常驻键盘驱动，输入法、游戏、长按连发全部进入射程。
   **代价明说**：命中的快捷键同时也会送达前台应用，需引导用户选不易冲突的组合键。

3. **绝对移动必须走 ABS 设备。** 相对位移会被合成器的指针加速曲线改写，位移量与请求值不等，
   自动点击回放的落点会逐步漂移。因此虚拟设备分三台（键盘 / REL 指针 / ABS 指针）——
   libinput 按能力判定设备类型，REL 与 ABS 混在一台上会被误判。

4. **截图走 Screenshot portal，`interactive=false` 抛整屏，选区仍由自绘遮罩完成。**
   Flameshot 与 kbinani/screenshot 两个独立项目收敛到同一个调用。这样放大镜、实时贴图、OCR、
   选区坐标回传全部保留，Linux 与 Windows 体验不分裂。

5. **不碰 PipeWire。** Wox 走的是 ScreenCast portal + PipeWire（927 行 Go + cgo），
   那笔复杂度买的是**录屏**能力，本应用没有录屏；且 GNOME 下它弹的是源选择器，比一个授权框更烦。

6. **Avalonia 升 12.1.1，Linux 显式 `UseX11()`。** 12.1 起 `Avalonia.Wayland` 已可用，
   但**纯 Wayland 协议里不存在「把窗口放到坐标 (x,y)」**，而环形菜单、翻译窗、截图预览、
   录制指示器全依赖按鼠标位置定位。`UIHARU_LINUX_BACKEND=wayland` 可切原生后端做对照实验。

7. **继续以 `SharpHook.Data` 作为输入领域的通用类型。** 它已渗透 18 个文件。
   Core 依赖它的**数据**（`KeyCode`/`MouseButton`/`MouseEventData`）而非**行为**
   （`GlobalHook`/`EventSimulator`），这条边界站得住；Linux 后端在边界上用
   `EvDevKeyCodeMapper` 完成翻译。抽 `UiharuKeyCode` 记为独立技术债。

## 全局坐标：删掉需求，而不是实现它

纯 Wayland 下客户端**没有任何合法途径**查询全局光标位置——指针事件只投递给焦点 surface，
常驻的全屏透明窗口要么因输入穿透收不到事件，要么霸占全屏点击。这是安全模型的有意设计。

关键在于：**就算坐标算得再准，纯 Wayland 下也放不了窗口**。于是逐条清算用途——

| 用途 | 处置 |
|---|---|
| 截图选区 | **不需要全局坐标**。遮罩窗铺满目标屏并独占指针，窗内事件坐标就是真值。`ScreenCaptureWindow` 已改为只用自己的 `PointerMoved/Released` |
| 自动点击录制回放 | evdev 录相对位移 → uinput 回放，绝对落点由 ABS 设备负责 |
| 按鼠标位置弹窗 | 唯一真正需要它的场景。由 `IPointerLocator` 供给；不可用时 `SetWindowToMousePosition` 统一退化为居中 |

`IPointerLocator` 有两个实现：`X11PointerLocator`（XWayland 的 `XQueryPointer`，带 8ms 缓存，
因为鼠标移动可达上千 Hz）与 `UnavailablePointerLocator`（纯 Wayland）。
**XWayland 若哪天消失，换的是一个实现类，不是翻全仓。**

## 取舍

- **接受 keylogger 级权限。** 加入 `input` 组等于获得读取全部键盘输入的能力。这是 Wayland 下
  实现全局快捷键的既定代价，`scripts/linux/setup-linux.sh` 的注释里对用户明说了这一点。

- **XWayland 依赖是有意保留的，不是疏忽。** GNOME 49 删掉的是 **Xorg 会话**，不是 **XWayland**；
  后者默认安装启用，Red Hat 在 RHEL 10 明确「移除 Xorg server 及其他 X server，**XWayland 除外**」，
  且无退役时间表。切原生 Wayland 后端会立刻打死所有定位窗口，收益为负。

- **Linux 下截图不跟随切屏。** 遮罩窗打开后鼠标移到另一块屏时，Windows 会重抓该屏；
  Linux 不会——重抓意味着再走一次 Portal，再弹一次授权框。取当前屏一张，用完为止。

- **Portal 的 `parent_window` 不能传空串。** `xdg-desktop-portal-gnome` 46 起会拒绝空句柄，
  授权框弹不出来。X11 下传 `x11:0x{XID}`（取自 `DummyWindow`），纯 Wayland 下传 `wayland:`。
  kbinani/screenshot 传的是空串，这一点**不能照抄**。

- **注入前等待物理修饰键释放。** 用户手上可能还按着触发快捷键的修饰键，此刻注入
  Ctrl+C 会变成 Alt+Ctrl+C。轮询上限 1 秒，等不到就照常注入，避免把回放卡死。

- **`ScreenCaptureLinux` 只保留 grim 一条兜底。** `gnome-screenshot`/`spectacle`/`scrot` 分支全删——
  第一条在目标环境上已是死代码，后两条永远不会被走到，属于养不动的死路径。
  grim 留给 wlroots 系没装 portal 后端的情况。

- **`Tmds.DBus.Protocol` 在 Core 显式声明。** 它本已由 `Avalonia.FreeDesktop` 传递引入，
  但 Core 不引用 Avalonia，故显式声明；用的是低层 API，为一个接口不值得再引高层 `Tmds.DBus`。

- **`Avalonia.Wayland` 仅为对照实验而引入**（连带 `NWayland`）。默认代码路径不碰它。
