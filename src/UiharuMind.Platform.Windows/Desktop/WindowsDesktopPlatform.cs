/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using UiharuMind.Core.Core.SimpleLog;
using static Interop.UIAutomationClient.UIA_ControlTypeIds;
using static Interop.UIAutomationClient.UIA_PatternIds;
using static UiharuMind.Platform.Windows.Desktop.WindowsNative;

namespace UiharuMind.Platform.Windows.Desktop;

/// <summary>
/// Windows 的桌面平台层（ADR 0084）：UI Automation 读树与动作（第 1 级），窗口消息投递（第 2 级），
/// 真实键鼠走应用的输入模拟（第 3 级，与自动点击同一套）。读树与动作不要额外授权；
/// 以更高权限运行的窗口（UIPI 挡着）不提权，报错交给用户
/// </summary>
internal sealed class WindowsDesktopPlatform : IDesktopPlatform
{
    private const double MinWindowSide = 40;
    private const int MaxWindows = 60;
    private const int WebTreeWarmUpMs = 1500;
    private const int InvokeTimeoutMs = 2000; //点开模态对话框的按钮，Invoke 要等对话框关了才回
    private const string FrameHostExe = "ApplicationFrameHost.exe"; //商店应用的窗口框，内容在另一个进程里

    // 桌面背景、任务栏这类壳窗口
    private static readonly HashSet<string> ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private readonly IUIAutomation _automation = new CUIAutomation8();
    private readonly ConcurrentDictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase); //可执行文件 → 给人看的应用名
    private readonly ConcurrentDictionary<int, bool> _webWarmedUp = new();

    /// <inheritdoc />
    public string? Unavailable => null;

    /// <inheritdoc />
    public string? DialogPathHint =>
        "fill the full path into the file name box, then Enter";

    /// <inheritdoc />
    public void RequestAccess()
    {
    }

    /// <inheritdoc />
    public double SecondsSinceUserInput
    {
        get
        {
            LastInputInfo info = new() { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) / 1000.0 : double.MaxValue;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DesktopWindowInfo> ListWindows()
    {
        List<IntPtr> handles = new();
        EnumWindows((hwnd, _) =>
        {
            handles.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        IntPtr foreground = GetForegroundWindow();
        List<(DesktopWindowInfo Info, int Order)> found = new();
        for (int order = 0; order < handles.Count && found.Count < MaxWindows; order++)
        {
            if (Describe(handles[order], foreground) is { } info) found.Add((info, info.Focused ? -1 : info.Minimized ? int.MaxValue : order));
        }

        return found.OrderBy(entry => entry.Order).Select(entry => entry.Info).ToList();
    }

    /// <inheritdoc />
    public bool SameWindow(DesktopWindowInfo a, DesktopWindowInfo b) => Hwnd(a) == Hwnd(b);

    /// <inheritdoc />
    public AxNode ReadWindow(DesktopWindowInfo window, int maxNodes)
    {
        IntPtr hwnd = Hwnd(window);
        if (!IsWindow(hwnd)) throw new DesktopWindowGoneException(window);
        WarmUpWebTree(window.Pid, hwnd);
        try
        {
            return UiaTreeReader.Read(_automation, hwnd, window.Title, maxNodes);
        }
        catch (COMException) when (!IsWindow(hwnd))
        {
            throw new DesktopWindowGoneException(window);
        }
    }

    /// <inheritdoc />
    public bool TryPerform(AxNode node, EDesktopAction action, string value)
    {
        if (node.Source is not UiaElement element) return false;
        try
        {
            return action switch
            {
                EDesktopAction.Click => Click(element),
                EDesktopAction.Fill => Fill(element, value),
                EDesktopAction.Focus => Focus(element),
                EDesktopAction.Select => Select(element, node, value),
                // UIA 没有「在光标处插入」：交给投递的字符消息
                _ => false,
            };
        }
        catch (COMException)
        {
            return false; //控件不认这个操作，或已经没了：退给下一级
        }
    }

    /// <inheritdoc />
    public void Input(DesktopWindowInfo window, DesktopInput input, bool real)
    {
        if (real) DesktopRealInput.Send(input);
        else WindowMessages.Post(Hwnd(window), input);
    }

    /// <inheritdoc />
    public byte[] CaptureWindow(DesktopWindowInfo window) => WindowCapture.Capture(Hwnd(window));

    /// <inheritdoc />
    public bool Raise(DesktopWindowInfo window)
    {
        IntPtr hwnd = Hwnd(window);
        if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;
        // 系统只许刚收到过输入的进程换前台：先按一下 Alt 拿到资格
        DesktopRealInput.TapAlt();
        SetForegroundWindow(hwnd);
        Thread.Sleep(50);
        return GetForegroundWindow() == hwnd;
    }

    /// <inheritdoc />
    public void Open(string target, bool isFile)
    {
        if (isFile)
        {
            Start(target, $"Could not open {target}.");
            return;
        }

        // 应用名：先让系统按「运行」那样找（notepad、calc、注册过的程序），找不到再翻开始菜单的快捷方式
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return;
        }
        catch (Win32Exception)
        {
        }

        string? shortcut = FindStartMenuShortcut(target);
        if (shortcut == null)
        {
            throw new AxActionException($"There is no app named \"{target}\". Use the name shown in the Start menu.");
        }

        Start(shortcut, $"Could not start {target}.");
    }

    private DesktopWindowInfo? Describe(IntPtr hwnd, IntPtr foreground)
    {
        if (!IsWindowVisible(hwnd) || IsCloaked(hwnd)) return null;
        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if ((style & WsExToolWindow) != 0 && (style & WsExAppWindow) == 0) return null;
        string title = WindowText(hwnd);
        if (title.Length == 0 || ShellClasses.Contains(ClassName(hwnd))) return null;

        bool minimized = IsIconic(hwnd);
        Rect rect = minimized ? NormalPosition(hwnd) : FrameBounds(hwnd);
        if (rect.Width < MinWindowSide || rect.Height < MinWindowSide) return null;

        GetWindowThreadProcessId(hwnd, out int pid);
        string path = ProcessPath(pid);
        // 商店应用：窗口框属于 ApplicationFrameHost，真正的应用（设置、计算器）在里面那个子窗口的进程
        if (string.Equals(Path.GetFileName(path), FrameHostExe, StringComparison.OrdinalIgnoreCase) && ContentProcess(hwnd, pid) is { } content)
        {
            pid = content;
            path = ProcessPath(pid);
        }

        if (pid == Environment.ProcessId) return null;
        string exe = path.Length > 0 ? Path.GetFileName(path) : ProcessName(pid) + ".exe";
        return new DesktopWindowInfo(pid, AppName(path, exe), exe, title, new AxRect(rect.Left, rect.Top, rect.Width, rect.Height))
        {
            Focused = hwnd == foreground,
            Minimized = minimized,
            Handle = hwnd,
        };
    }

    private bool Click(UiaElement element)
    {
        // 列表项、标签页、树节点：「点一下」是选中。列表项的 Invoke 是打开（资源管理器里就是双击）
        if (element.CanSelect && element.ControlType is UIA_ListItemControlTypeId or UIA_DataItemControlTypeId or
                UIA_TabItemControlTypeId or UIA_TreeItemControlTypeId)
        {
            Pattern<IUIAutomationSelectionItemPattern>(element, UIA_SelectionItemPatternId).Select();
            return true;
        }

        if (element.CanInvoke) return WithTimeout(() => Pattern<IUIAutomationInvokePattern>(element, UIA_InvokePatternId).Invoke());
        if (element.CanToggle)
        {
            Pattern<IUIAutomationTogglePattern>(element, UIA_TogglePatternId).Toggle();
            return true;
        }

        if (element.CanSelect)
        {
            Pattern<IUIAutomationSelectionItemPattern>(element, UIA_SelectionItemPatternId).Select();
            return true;
        }

        if (element.CanExpand)
        {
            IUIAutomationExpandCollapsePattern expand = Pattern<IUIAutomationExpandCollapsePattern>(element, UIA_ExpandCollapsePatternId);
            if (expand.CurrentExpandCollapseState == ExpandCollapseState.ExpandCollapseState_Collapsed) expand.Expand();
            else expand.Collapse();
            return true;
        }

        return element.HasDefaultAction &&
               WithTimeout(() => Pattern<IUIAutomationLegacyIAccessiblePattern>(element, UIA_LegacyIAccessiblePatternId).DoDefaultAction());
    }

    private static bool Fill(UiaElement element, string value)
    {
        if (!element.CanSetValue) return false;
        Pattern<IUIAutomationValuePattern>(element, UIA_ValuePatternId).SetValue(value);
        return true;
    }

    private static bool Focus(UiaElement element)
    {
        element.Element.SetFocus();
        return true;
    }

    // 可编辑的下拉框直接写值；别的展开后按字选那一项；列表、表格、树按字选那一行
    private bool Select(UiaElement element, AxNode node, string value)
    {
        if (element.ControlType == UIA_ComboBoxControlTypeId)
        {
            if (element.CanSetValue) return Fill(element, value);
            if (!element.CanExpand) return false;
            IUIAutomationExpandCollapsePattern expand = Pattern<IUIAutomationExpandCollapsePattern>(element, UIA_ExpandCollapsePatternId);
            expand.Expand();
            IUIAutomationElement? item = element.Element.FindFirst(TreeScope.TreeScope_Descendants,
                _automation.CreatePropertyCondition(UIA_PropertyIds.UIA_NamePropertyId, value));
            if (item?.GetCurrentPattern(UIA_SelectionItemPatternId) is IUIAutomationSelectionItemPattern option)
            {
                option.Select();
                expand.Collapse();
                return true;
            }

            expand.Collapse();
            return false;
        }

        string wanted = AxRoles.Flatten(value);
        AxNode? row = Find(node, candidate => candidate.Source is UiaElement { CanSelect: true } && Mentions(candidate, wanted));
        if (row?.Source is not UiaElement target) return false;
        Pattern<IUIAutomationSelectionItemPattern>(target, UIA_SelectionItemPatternId).Select();
        return true;
    }

    // Chromium 与 Electron 见到 UIA 客户端才建网页那半棵树，要过一会儿：每个进程头一次读时等一等
    private void WarmUpWebTree(int pid, IntPtr hwnd)
    {
        if (!ClassName(hwnd).StartsWith("Chrome_WidgetWin", StringComparison.Ordinal) || !_webWarmedUp.TryAdd(pid, true)) return;
        _automation.ElementFromHandle(hwnd);
        Thread.Sleep(WebTreeWarmUpMs);
    }

    private string AppName(string path, string exe) => _appNames.GetOrAdd(path.Length > 0 ? path : exe, _ =>
    {
        string? description = path.Length > 0 ? FileVersionInfo.GetVersionInfo(path).FileDescription : null;
        return string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(exe) : description.Trim();
    });

    private static int? ContentProcess(IntPtr frame, int framePid)
    {
        int? content = null;
        EnumChildWindows(frame, (child, _) =>
        {
            GetWindowThreadProcessId(child, out int pid);
            if (pid == framePid) return true;
            content = pid;
            return false;
        }, IntPtr.Zero);
        return content;
    }

    private static Rect NormalPosition(IntPtr hwnd)
    {
        WindowPlacement placement = new() { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
        return GetWindowPlacement(hwnd, ref placement) ? placement.NormalPosition : default;
    }

    private static string ProcessName(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "unknown";
        }
    }

    private static string? FindStartMenuShortcut(string name)
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        ];
        List<string> shortcuts = roots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            .ToList();
        return shortcuts.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
               ?? shortcuts.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static void Start(string target, string failure)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Win32Exception e)
        {
            throw new AxActionException($"{failure} {e.Message}");
        }
    }

    // 有的控件的调用要等它弹出的模态框关掉才返回：等一会儿就当成功了，让模型看结果
    private static bool WithTimeout(Action action)
    {
        Task task = Task.Run(action);
        try
        {
            if (!task.Wait(InvokeTimeoutMs))
            {
                // 超时当成成功是设计取舍（让模型看结果）；但按钮没弹模态框时这就是误报，留一条日志好排查
                Log.Warning($"Desktop 控件 Invoke 超时（{InvokeTimeoutMs} ms）未返回，按成功处理；若对话框并未弹出，该步可能没生效。");
            }

            return true;
        }
        catch (AggregateException e) when (e.InnerException is COMException)
        {
            return false;
        }
    }

    private static T Pattern<T>(UiaElement element, int patternId) where T : class =>
        element.Element.GetCurrentPattern(patternId) as T ?? throw new COMException("Pattern not available.");

    private static bool Mentions(AxNode node, string wanted) =>
        string.Equals(AxRoles.Flatten(node.Name), wanted, StringComparison.OrdinalIgnoreCase) ||
        node.Children.Any(child => Mentions(child, wanted));

    private static AxNode? Find(AxNode node, Func<AxNode, bool> predicate)
    {
        if (predicate(node)) return node;
        foreach (AxNode child in node.Children)
        {
            if (Find(child, predicate) is { } found) return found;
        }

        return null;
    }

    private static IntPtr Hwnd(DesktopWindowInfo window) =>
        window.Handle as IntPtr? ?? throw new AxActionException("That window is no longer available. List the windows again.");
}
