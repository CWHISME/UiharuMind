using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;

namespace UiharuMind.Core.Tests.Execution.Desktop;

/// <summary>
/// 假平台：窗口与树由测试给，动作只记下来
/// </summary>
internal sealed class FakeDesktopPlatform : IDesktopPlatform
{
    public List<DesktopWindowInfo> Windows { get; } = new();

    public Dictionary<string, AxNode> Trees { get; } = new(); //窗口标题 → 树

    public HashSet<string> ClosedTitles { get; } = new();

    public HashSet<string> UnresponsiveTitles { get; } = new(); //应用卡住：读它时报错，但不是窗口关闭

    public bool AccessibilityActionsWork { get; set; } = true;

    public List<(string Node, EDesktopAction Action, string Value)> Performed { get; } = new();

    public List<(DesktopInput Input, bool Real)> Inputs { get; } = new();

    public List<string> Raised { get; } = new();

    public List<(string Target, bool IsFile)> Opened { get; } = new();

    public string? Unavailable { get; set; }

    public byte[] Picture { get; set; } = [1, 2, 3];

    public Queue<byte[]> NextPictures { get; } = new(); //接下来几次截图依次给的画面，给完回到 Picture

    public string? CaptureRefusal { get; set; } //截图时报的错（没有屏幕录制授权）

    public int Captures { get; private set; }

    public double SecondsSinceUserInput { get; set; } = 60;

    public void RequestAccess()
    {
    }

    public IReadOnlyList<DesktopWindowInfo> ListWindows() => Windows.ToList();

    public bool SameWindow(DesktopWindowInfo a, DesktopWindowInfo b) => Equals(a.Handle, b.Handle);

    public AxNode ReadWindow(DesktopWindowInfo window, int maxNodes) =>
        ClosedTitles.Contains(window.Title) ? throw new DesktopWindowGoneException(window)
        : UnresponsiveTitles.Contains(window.Title) ? throw new AxActionException($"{window.Title} is not responding right now; try again.")
        : Trees[window.Title];

    public bool TryPerform(AxNode node, EDesktopAction action, string value)
    {
        if (!AccessibilityActionsWork) return false;
        Performed.Add((node.Name, action, value));
        return true;
    }

    public void Input(DesktopWindowInfo window, DesktopInput input, bool real) => Inputs.Add((input, real));

    public byte[] CaptureWindow(DesktopWindowInfo window)
    {
        Captures++;
        if (CaptureRefusal != null) throw new AxActionException(CaptureRefusal);
        return NextPictures.TryDequeue(out byte[]? next) ? next : Picture;
    }

    public bool Raise(DesktopWindowInfo window)
    {
        Raised.Add(window.Title);
        return true;
    }

    public void Open(string target, bool isFile) => Opened.Add((target, isFile));

    /// <summary>
    /// 加一个窗口
    /// </summary>
    public DesktopWindowInfo AddWindow(string app, string appId, string title, AxNode tree, AxRect? bounds = null)
    {
        DesktopWindowInfo info = new(Windows.Count + 100, app, appId, title, bounds ?? new AxRect(100, 50, 800, 600))
        {
            Handle = title,
        };
        Windows.Add(info);
        Trees[title] = tree;
        return info;
    }
}
