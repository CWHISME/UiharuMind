/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.Diagnostics;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using static UiharuMind.Core.AI.Execution.Tools.Desktop.Mac.MacNative;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop.Mac;

/// <summary>
/// macOS 的桌面平台层：AX 读树与动作（第 1 级），CGEvent 投递给进程（第 2 级）或发到系统（第 3 级）。
/// 只用公开接口；后台点击靠在事件上写明窗口号，不用 SkyLight（ADR 0084）。
/// 授权落在应用自己的签名上（ADR 0029 的自签名证书保证升级不丢）
/// </summary>
internal sealed class MacDesktopPlatform : IDesktopPlatform
{
    private const float MessagingTimeoutSeconds = 1.5f; //应用卡住时一次询问最多等这么久
    private const float ListingTimeoutSeconds = 0.5f; //列窗口要问遍每个应用，单个卡住的不能拖住整张表
    private const double MinWindowSide = 40;
    private const int MaxApps = 40;
    private const int WebTreeWarmUpMs = 1500;

    private readonly ConcurrentDictionary<int, string> _bundleIds = new();
    private readonly ConcurrentDictionary<int, bool> _webAccessibilityEnabled = new();
    private int _captureRequested; //请系统弹过屏幕录制授权框没有
    private readonly Lazy<IntPtr> _eventSource = new(() => CGEventSourceCreate(EventSourcePrivate)); //用到才建：建它就会触发「发送事件」的授权检查

    public MacDesktopPlatform()
    {
        AXUIElementSetMessagingTimeout(AXUIElementCreateSystemWide(), MessagingTimeoutSeconds);
    }

    /// <inheritdoc />
    public string? Unavailable => AXIsProcessTrusted() != 0
        ? null
        : "Accessibility permission has not been granted to this app. Ask the user to allow it in System Settings → " +
          "Privacy & Security → Accessibility (enabling the desktop extension in Settings opens the prompt), then try again.";

    /// <inheritdoc />
    public void RequestAccess()
    {
        PromptForTrust();
        CGRequestPostEventAccess();
    }

    /// <inheritdoc />
    public double SecondsSinceUserInput => CGEventSourceSecondsSinceLastEventType(EventSourceHidSystem, AnyInputEvent);

    /// <inheritdoc />
    public IReadOnlyList<DesktopWindowInfo> ListWindows()
    {
        List<CgWindow> cgWindows = ListCgWindows();
        int frontPid = FrontmostPid(out MacAxElement? frontWindow);
        IEnumerable<int> pids = cgWindows.Select(window => window.Pid).Distinct().Take(MaxApps);

        List<(DesktopWindowInfo Info, int Order)> found = new();
        foreach (int pid in pids)
        {
            MacAxElement app = MacAxElement.Adopt(AXUIElementCreateApplication(pid))!;
            AXUIElementSetMessagingTimeout(app.Handle, ListingTimeoutSeconds);
            string appName = cgWindows.First(window => window.Pid == pid).Owner;
            foreach (MacAxElement window in app.GetElements("AXWindows"))
            {
                if (ReadWindow(window, app, pid, appName, cgWindows, frontPid == pid && frontWindow != null &&
                        window.SameAs(frontWindow)) is { } info)
                {
                    int order = info.Focused ? -1 : (info.Handle as MacWindowHandle)!.ZOrder;
                    found.Add((info, info.Minimized ? int.MaxValue : order));
                }
            }

            AXUIElementSetMessagingTimeout(app.Handle, MessagingTimeoutSeconds);
        }

        return found.OrderBy(entry => entry.Order).Select(entry => entry.Info).ToList();
    }

    /// <inheritdoc />
    public bool SameWindow(DesktopWindowInfo a, DesktopWindowInfo b) =>
        a.Pid == b.Pid && a.Handle is MacWindowHandle x && b.Handle is MacWindowHandle y && x.Element.SameAs(y.Element);

    /// <inheritdoc />
    public AxNode ReadWindow(DesktopWindowInfo window, int maxNodes)
    {
        MacWindowHandle handle = Handle(window);
        if (handle.Element.Get("AXRole") == null) throw new DesktopWindowGoneException(window);
        EnableWebAccessibility(window.Pid, handle.App);
        return MacAxTreeReader.Read(handle.Element, handle.App, window.Title, maxNodes);
    }

    /// <inheritdoc />
    public bool TryPerform(AxNode node, EDesktopAction action, string value)
    {
        if (node.Source is not MacAxElement element) return false;
        switch (action)
        {
            case EDesktopAction.Click:
                // Chromium 系对网页元素的 AXPress 回报成功却什么也没点：交给事件
                if (element.InWebArea) return false;
                int pressed = element.Perform("AXPress");
                // 点开菜单栏的菜单时，应用进了菜单的事件循环，这次询问会等到超时才回：菜单其实已经开了
                if (pressed == AxSuccess || (pressed == AxErrorCannotComplete && element.Role == "AXMenuBarItem")) return true;
                return element.Role is "AXRow" or "AXCell" && element.Set("AXSelected", True) == AxSuccess;

            case EDesktopAction.Fill:
                if (!element.IsSettable("AXValue")) return false;
                element.Set("AXFocused", True);
                return element.SetString("AXValue", value) == AxSuccess;

            case EDesktopAction.Type:
                // 在光标处插入：把「选中的字」换成要打的字。后台应用没有键盘窗口，按键事件会被丢掉，这条路不经键盘
                if (!element.IsSettable("AXSelectedText")) return false;
                element.Set("AXFocused", True);
                return element.SetString("AXSelectedText", value) == AxSuccess;

            case EDesktopAction.Focus:
                return element.Set("AXFocused", True) == AxSuccess;

            case EDesktopAction.Select:
                return Select(element, node, value);

            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void Input(DesktopWindowInfo window, DesktopInput input, bool real)
    {
        // 读树、无障碍动作只要无障碍授权；发键鼠事件另要一道「辅助功能（事件）」授权，没有时系统静默丢弃
        if (CGPreflightPostEventAccess() == 0)
        {
            throw new AxActionException("This step needs to send key or mouse events, which the user has not allowed yet " +
                                        "(System Settings → Privacy & Security → Accessibility). Steps that work through the " +
                                        $"accessibility interface still work; otherwise use {DesktopTools.HandoffMention}.");
        }

        MacWindowHandle handle = Handle(window);
        switch (input.Kind)
        {
            case EDesktopInputKind.Click:
                Click(window.Pid, handle.WindowNumber, input, real);
                break;
            case EDesktopInputKind.Key:
                (ushort keyCode, ulong flags) = MacKeys.Resolve(input.Keys);
                Key(window.Pid, keyCode, flags, real);
                break;
            case EDesktopInputKind.SelectAll:
                Key(window.Pid, MacKeys.KeyA, MacKeys.FlagCommand, real);
                break;
            case EDesktopInputKind.Text:
                Type(window.Pid, input.Text, real);
                break;
            case EDesktopInputKind.Scroll:
                Scroll(window.Pid, handle.WindowNumber, input, real);
                break;
        }
    }

    /// <inheritdoc />
    public byte[] CaptureWindow(DesktopWindowInfo window)
    {
        if (CGPreflightScreenCaptureAccess() == 0)
        {
            // 第一次请系统弹授权框；授了也要重启本应用才生效（系统的规矩）
            if (Interlocked.Exchange(ref _captureRequested, 1) == 0) CGRequestScreenCaptureAccess();
            throw new AxActionException("Reading the window's picture needs the Screen Recording permission, which the user " +
                                        "has not allowed yet (System Settings → Privacy & Security → Screen Recording; " +
                                        "the app must be restarted after allowing it).");
        }

        uint number = Handle(window).WindowNumber;
        if (number == 0) throw new AxActionException("This window can't be captured: the system did not report it on screen.");
        string path = Path.Combine(Path.GetTempPath(), $"uiharu-desktop-{Guid.NewGuid():N}.png");
        try
        {
            // -l 只截这个窗口（压在上面的不截），-o 不带阴影，-x 不出声
            ProcessStartInfo start = new("/usr/sbin/screencapture") { UseShellExecute = false };
            foreach (string argument in new[] { "-x", "-o", "-t", "png", "-l", number.ToString(), path }) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            process.WaitForExit(10_000);
            return File.Exists(path) && new FileInfo(path).Length > 0
                ? File.ReadAllBytes(path)
                : throw new AxActionException("Capturing the window failed (it may be minimized or on another Space).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc />
    public bool Raise(DesktopWindowInfo window)
    {
        MacWindowHandle handle = Handle(window);
        if (window.Minimized) handle.Element.Set("AXMinimized", False);
        handle.App.Set("AXFrontmost", True);
        return handle.Element.Perform("AXRaise") == AxSuccess;
    }

    /// <inheritdoc />
    public void Open(string target, bool isFile)
    {
        // -g：在后台打开，不抢用户的前台；要看时用户自己切过去，或接管时提到前台
        ProcessStartInfo start = new("/usr/bin/open") { RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-g");
        if (!isFile) start.ArgumentList.Add("-a");
        start.ArgumentList.Add(target);
        using Process process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        if (process.ExitCode != 0)
        {
            throw new AxActionException(isFile
                ? $"Could not open {target}: {error.Trim()}"
                : $"There is no app named \"{target}\". Use the name shown in Finder's Applications folder.");
        }
    }

    private DesktopWindowInfo? ReadWindow(MacAxElement window, MacAxElement app, int pid, string appName,
        List<CgWindow> cgWindows, bool focused)
    {
        string role = window.Get("AXRole") as string ?? string.Empty;
        if (role != "AXWindow") return null;
        if (window.Get("AXPosition") is not CGPoint position || window.Get("AXSize") is not CGPoint size) return null;
        if (size.X < MinWindowSide || size.Y < MinWindowSide) return null;

        AxRect bounds = new(position.X, position.Y, size.X, size.Y);
        CgWindow? cg = cgWindows.FirstOrDefault(candidate => candidate.Pid == pid && Near(candidate.Bounds, bounds));
        string title = window.Get("AXTitle") as string ?? string.Empty;
        return new DesktopWindowInfo(pid, appName, BundleId(pid, appName), title, bounds)
        {
            Focused = focused,
            Minimized = window.Get("AXMinimized") is true,
            Handle = new MacWindowHandle(window, app, cg?.Number ?? 0, cg?.ZOrder ?? int.MaxValue - 1),
        };
    }

    // 下拉框：按下展开，在弹出的菜单里按字点那一项；列表、表格：把那一行设为选中
    private static bool Select(MacAxElement element, AxNode node, string value)
    {
        string wanted = AxRoles.Flatten(value);
        if (element.Role == "AXComboBox" && element.IsSettable("AXValue")) return element.SetString("AXValue", value) == AxSuccess;
        if (element.Role == "AXPopUpButton")
        {
            if (element.Perform("AXPress") is not (AxSuccess or AxErrorCannotComplete)) return false;
            foreach (MacAxElement menu in element.GetElements("AXChildren"))
            {
                foreach (MacAxElement item in menu.GetElements("AXChildren"))
                {
                    if (Matches(item.Get("AXTitle") as string, wanted)) return item.Perform("AXPress") == AxSuccess;
                }

                menu.Perform("AXCancel");
            }

            return false;
        }

        AxNode? row = Find(node, candidate => candidate.Source is MacAxElement { Role: "AXRow" } && Mentions(candidate, wanted));
        return row?.Source is MacAxElement rowElement && rowElement.Set("AXSelected", True) == AxSuccess;
    }

    private static bool Mentions(AxNode node, string wanted) =>
        Matches(node.Name, wanted) || node.Children.Any(child => Mentions(child, wanted));

    private static bool Matches(string? text, string wanted) =>
        text != null && string.Equals(AxRoles.Flatten(text), wanted, StringComparison.OrdinalIgnoreCase);

    private static AxNode? Find(AxNode node, Func<AxNode, bool> predicate)
    {
        if (predicate(node)) return node;
        foreach (AxNode child in node.Children)
        {
            if (Find(child, predicate) is { } found) return found;
        }

        return null;
    }

    private void Click(int pid, uint windowNumber, DesktopInput input, bool real)
    {
        CGPoint point = new(input.X, input.Y);
        (uint down, uint up, uint button) = input.RightButton
            ? (EventRightMouseDown, EventRightMouseUp, MouseButtonRight)
            : (EventLeftMouseDown, EventLeftMouseUp, MouseButtonLeft);
        if (real) Post(pid, CGEventCreateMouseEvent(_eventSource.Value, EventMouseMoved, point, button), real, 0);
        for (int click = 1; click <= Math.Max(1, input.Clicks); click++)
        {
            foreach (uint type in new[] { down, up })
            {
                IntPtr cgEvent = CGEventCreateMouseEvent(_eventSource.Value, type, point, button);
                CGEventSetIntegerValueField(cgEvent, FieldMouseClickState, click);
                Post(pid, cgEvent, real, windowNumber);
            }
        }
    }

    private void Key(int pid, ushort keyCode, ulong flags, bool real)
    {
        foreach (bool down in new[] { true, false })
        {
            IntPtr cgEvent = CGEventCreateKeyboardEvent(_eventSource.Value, keyCode, down ? (byte)1 : (byte)0);
            CGEventSetFlags(cgEvent, flags);
            Post(pid, cgEvent, real, 0);
        }
    }

    // 不按键位打：直接带上要打的字，输入法、键盘布局都不碍事
    private void Type(int pid, string text, bool real)
    {
        foreach (string piece in Pieces(text))
        {
            char[] chars = piece.ToCharArray();
            foreach (bool down in new[] { true, false })
            {
                IntPtr cgEvent = CGEventCreateKeyboardEvent(_eventSource.Value, 0, down ? (byte)1 : (byte)0);
                CGEventKeyboardSetUnicodeString(cgEvent, (nuint)chars.Length, chars);
                Post(pid, cgEvent, real, 0);
            }

            Thread.Sleep(8);
        }
    }

    // 换行要按回车键，不能当字符塞；一次事件最多带 20 个字
    private static IEnumerable<string> Pieces(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n").Split('\n').SelectMany((line, index) =>
                     index == 0 ? new[] { line } : new[] { "\n", line }))
        {
            if (line == "\n")
            {
                yield return "\r";
                continue;
            }

            for (int start = 0; start < line.Length; start += 20) yield return line.Substring(start, Math.Min(20, line.Length - start));
        }
    }

    // 系统把滚轮发给指针下的窗口、不看事件里的位置：真实滚动先把指针挪过去
    private void Scroll(int pid, uint windowNumber, DesktopInput input, bool real)
    {
        CGPoint point = new(input.X, input.Y);
        if (real) Post(pid, CGEventCreateMouseEvent(_eventSource.Value, EventMouseMoved, point, MouseButtonLeft), real, 0);
        IntPtr cgEvent = CGEventCreateScrollWheelEvent2(_eventSource.Value, ScrollUnitLine, 1, -input.ScrollLines, 0, 0);
        CGEventSetLocation(cgEvent, point);
        Post(pid, cgEvent, real, windowNumber);
    }

    // 第 2 级投递给进程并写明窗口号（应用据此认出点的是哪个窗口，后台窗口也认）；第 3 级发到系统
    private static void Post(int pid, IntPtr cgEvent, bool real, uint windowNumber)
    {
        try
        {
            if (real)
            {
                CGEventPost(HidEventTap, cgEvent);
            }
            else
            {
                if (windowNumber != 0)
                {
                    CGEventSetIntegerValueField(cgEvent, FieldWindowUnderPointer, windowNumber);
                    CGEventSetIntegerValueField(cgEvent, FieldWindowUnderPointerThatCanHandle, windowNumber);
                }

                CGEventPostToPid(pid, cgEvent);
            }

            Thread.Sleep(15);
        }
        finally
        {
            CFRelease(cgEvent);
        }
    }

    // Chromium 与 Electron 不开这两个开关就只给半棵树；对别的应用无害。
    // 开了之后网页那半棵树要一会儿才建好：头一次开且应用认这个开关时等一等
    private void EnableWebAccessibility(int pid, MacAxElement app)
    {
        if (!_webAccessibilityEnabled.TryAdd(pid, true)) return;
        bool manual = app.Set("AXManualAccessibility", True) == AxSuccess;
        app.Set("AXEnhancedUserInterface", True);
        if (manual) Thread.Sleep(WebTreeWarmUpMs);
    }

    private static int FrontmostPid(out MacAxElement? focusedWindow)
    {
        focusedWindow = null;
        MacAxElement? system = MacAxElement.Adopt(AXUIElementCreateSystemWide());
        MacAxElement? app = system?.GetElement("AXFocusedApplication");
        if (app == null || AXUIElementGetPid(app.Handle, out int pid) != AxSuccess) return 0;
        focusedWindow = app.GetElement("AXFocusedWindow");
        return pid;
    }

    private string BundleId(int pid, string appName) => _bundleIds.GetOrAdd(pid, _ =>
    {
        // 可执行文件在「包/Contents/MacOS/」下；包不一定叫 .app（Edge 从签名副本 .app.bundle 里跑）
        string path = ProcessPath(pid);
        DirectoryInfo? macOs = path.Length > 0 ? Directory.GetParent(path) : null;
        string? id = macOs is { Name: "MacOS", Parent: { Name: "Contents", Parent: { } bundle } }
            ? BundleIdentifier(bundle.FullName)
            : null;
        return id ?? (path.Length > 0 ? Path.GetFileName(path) : appName);
    });

    private static List<CgWindow> ListCgWindows()
    {
        IntPtr list = CGWindowListCopyWindowInfo(WindowListOptionAll | WindowListExcludeDesktop, 0);
        List<CgWindow> windows = new();
        if (list == IntPtr.Zero) return windows;
        try
        {
            IntPtr[] items = ArrayItems(list);
            for (int i = 0; i < items.Length; i++)
            {
                IntPtr info = items[i];
                if (ToManaged(DictionaryValue(info, "kCGWindowLayer")) is not 0d) continue;
                if (ToManaged(DictionaryValue(info, "kCGWindowOwnerPID")) is not double pid || (int)pid == Environment.ProcessId) continue;
                if (ToManaged(DictionaryValue(info, "kCGWindowNumber")) is not double number) continue;
                IntPtr boundsDictionary = DictionaryValue(info, "kCGWindowBounds");
                AxRect bounds = new(Number(boundsDictionary, "X"), Number(boundsDictionary, "Y"),
                    Number(boundsDictionary, "Width"), Number(boundsDictionary, "Height"));
                if (bounds.Width < MinWindowSide || bounds.Height < MinWindowSide) continue;
                string owner = ToManaged(DictionaryValue(info, "kCGWindowOwnerName")) as string ?? string.Empty;
                windows.Add(new CgWindow((int)pid, (uint)number, owner, bounds, i));
            }
        }
        finally
        {
            CFRelease(list);
        }

        return windows;
    }

    private static double Number(IntPtr dictionary, string key) =>
        dictionary != IntPtr.Zero && ToManaged(DictionaryValue(dictionary, key)) is double value ? value : 0;

    private static bool Near(AxRect a, AxRect b) =>
        Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2 && Math.Abs(a.Width - b.Width) <= 2 &&
        Math.Abs(a.Height - b.Height) <= 2;

    private static MacWindowHandle Handle(DesktopWindowInfo window) =>
        window.Handle as MacWindowHandle ?? throw new AxActionException("That window is no longer available. List the windows again.");

    /// <summary>
    /// CGWindowList 里的一个窗口：窗口号用来在后台投递点击，序号是前后叠放的次序
    /// </summary>
    private sealed record CgWindow(int Pid, uint Number, string Owner, AxRect Bounds, int ZOrder);

    /// <summary>
    /// 平台侧的窗口句柄
    /// </summary>
    private sealed record MacWindowHandle(MacAxElement Element, MacAxElement App, uint WindowNumber, int ZOrder);
}
