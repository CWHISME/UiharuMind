using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;

namespace UiharuMind.Core.Tests.Execution.Desktop;

/// <summary>
/// 桌面宿主（ADR 0084）：窗口编号、拒绝名单、三级动作逐级退、密码只经用户的手
/// </summary>
public class DesktopHostTests
{
    private readonly FakeDesktopPlatform _platform = new();
    private readonly DesktopHost _host;

    public DesktopHostTests()
    {
        _host = new DesktopHost(_platform) { SettleDelayMs = 1 };
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static AxNode Form() => new("window", "Note")
    {
        Children =
        [
            new AxNode("button", "Save") { Bounds = new AxRect(110, 60, 40, 20), Source = "native" },
            new AxNode("textbox", "Body") { Bounds = new AxRect(110, 100, 400, 300), Source = "native" },
            new AxNode("textbox", "Password") { Secret = true, Bounds = new AxRect(110, 420, 200, 20), Source = "native" },
            new AxNode("checkbox", "Wrap") { Checked = "true", Bounds = new AxRect(110, 450, 20, 20), Source = "native" },
        ],
    };

    [Fact]
    public async Task WindowIds_AreReusedAcrossListings_AndOffLimitsAppsAreMarked()
    {
        _platform.AddWindow("TextEdit", "com.apple.TextEdit", "Note", Form());
        _platform.AddWindow("Terminal", "com.apple.Terminal", "zsh", Form());

        IReadOnlyList<DesktopWindow> first = await _host.ListWindowsAsync(Token);
        _platform.AddWindow("Calculator", "com.apple.calculator", "Calc", Form());
        IReadOnlyList<DesktopWindow> second = await _host.ListWindowsAsync(Token);

        Assert.Equal(first[0].Id, second[0].Id);
        Assert.Equal("w3", second[2].Id);
        Assert.NotNull(second[1].Blocked);
        AxActionException error = await Assert.ThrowsAsync<AxActionException>(() => _host.SnapshotAsync("s", "Terminal", Token));
        Assert.Contains("can't be read or controlled", error.Message);
    }

    [Fact]
    public async Task Click_UsesTheAccessibilityAction_WhenTheControlSupportsIt()
    {
        string window = await OpenNoteAsync();

        DesktopView view = await _host.ActAsync("s", new DesktopActionRequest(window, EDesktopAction.Click, "e1", "", false), Token);

        Assert.Equal(("Save", EDesktopAction.Click, ""), Assert.Single(_platform.Performed));
        Assert.Empty(_platform.Inputs);
        Assert.Empty(view.Notes);
    }

    [Fact]
    public async Task Click_FallsBackToABackgroundClickAtTheCenter_AndSaysSo()
    {
        string window = await OpenNoteAsync();
        _platform.AccessibilityActionsWork = false;

        DesktopView view = await _host.ActAsync("s", new DesktopActionRequest(window, EDesktopAction.Click, "e1", "", false), Token);

        (DesktopInput input, bool real) = Assert.Single(_platform.Inputs);
        Assert.False(real);
        Assert.Equal((130d, 70d), (input.X, input.Y));
        Assert.Contains("input \"real\"", Assert.Single(view.Notes));
    }

    [Fact]
    public async Task RealInput_SkipsTheAccessibilityAction_AndRaisesTheWindowFirst()
    {
        string window = await OpenNoteAsync();

        await _host.ActAsync("s", new DesktopActionRequest(window, EDesktopAction.Click, "e1", "", true), Token);

        Assert.Empty(_platform.Performed);
        Assert.Equal(["Note"], _platform.Raised);
        Assert.True(Assert.Single(_platform.Inputs).Real);
    }

    [Fact]
    public async Task Coordinates_AreRelativeToTheWindow_AndMustBeInsideIt()
    {
        string window = await OpenNoteAsync();

        await _host.ActAsync("s", new DesktopActionRequest(window, EDesktopAction.Click, "10, 20", "", false), Token);
        DesktopInput input = Assert.Single(_platform.Inputs).Input;
        Assert.Equal((110d, 70d), (input.X, input.Y));

        await Assert.ThrowsAsync<AxActionException>(() =>
            _host.ActAsync("s", new DesktopActionRequest(window, EDesktopAction.Click, "900,10", "", false), Token));
    }

    [Theory]
    [InlineData(EDesktopAction.Fill, "e3", "hunter2")]
    [InlineData(EDesktopAction.Type, "e3", "hunter2")]
    [InlineData(EDesktopAction.Press, "e3", "a")]
    public async Task PasswordFields_AreNeverTypedInto(EDesktopAction action, string target, string value)
    {
        string window = await OpenNoteAsync();

        AxActionException error = await Assert.ThrowsAsync<AxActionException>(() =>
            _host.ActAsync("s", new DesktopActionRequest(window, action, target, value, false), Token));

        Assert.Contains("password", error.Message);
        Assert.Empty(_platform.Performed);
        Assert.Empty(_platform.Inputs);
    }

    [Fact]
    public async Task TypingWithoutATarget_IsRefused_WhenTheFocusIsInAPasswordField()
    {
        _platform.AddWindow("App", "app", "Login", new AxNode("window", "Login")
        {
            Children = [new AxNode("textbox", "Password") { Secret = true, Focused = true }],
        });
        await _host.ListWindowsAsync(Token);
        await _host.SnapshotAsync("s", "w1", Token);

        await Assert.ThrowsAsync<AxActionException>(() =>
            _host.ActAsync("s", new DesktopActionRequest("w1", EDesktopAction.Type, "", "secret", false), Token));
        // 回车不写字，放行
        await _host.ActAsync("s", new DesktopActionRequest("w1", EDesktopAction.Press, "", "Enter", false), Token);
    }

    [Fact]
    public async Task Check_OnAnAlreadyCheckedBox_DoesNothing()
    {
        string window = await OpenNoteAsync();

        DesktopView view = await _host.ActAsync("s", new DesktopActionRequest(window, EDesktopAction.Check, "e4", "", false), Token);

        Assert.Empty(_platform.Performed);
        Assert.Contains("already checked", Assert.Single(view.Notes));
    }

    [Fact]
    public async Task ActingBeforeASnapshot_AsksForOne()
    {
        _platform.AddWindow("TextEdit", "com.apple.TextEdit", "Note", Form());
        await _host.ListWindowsAsync(Token);

        AxActionException error = await Assert.ThrowsAsync<AxActionException>(() =>
            _host.ActAsync("s", new DesktopActionRequest("w1", EDesktopAction.Click, "e1", "", false), Token));

        Assert.Contains("snapshot of w1 first", error.Message);
    }

    [Fact]
    public async Task AStepThatClosesTheWindow_IsReportedNotFailed()
    {
        string window = await OpenNoteAsync();
        _platform.ClosedTitles.Add("Note");

        DesktopView view = await _host.ActAsync("s", new DesktopActionRequest(window, EDesktopAction.Click, "e1", "", false), Token);

        Assert.Empty(view.Snapshot);
        Assert.Contains("closed after that step", Assert.Single(view.Notes));
    }

    [Fact]
    public async Task AnUnresponsiveApp_IsNotReportedAsClosed_AndKeepsItsWindow()
    {
        string window = await OpenNoteAsync();
        await _host.SnapshotAsync("s", window, Token);

        _platform.UnresponsiveTitles.Add("Note");

        // 应用卡住不是窗口关闭：报「没响应」，不抛窗口关闭的错
        AxActionException error = await Assert.ThrowsAsync<AxActionException>(() => _host.SnapshotAsync("s", window, Token));
        Assert.Contains("not responding", error.Message);
        Assert.DoesNotContain("closed", error.Message);

        // 窗口还在列表里、编号没作废
        Assert.NotNull(_host.FindWindow(window));
        IReadOnlyList<DesktopWindow> listed = await _host.ListWindowsAsync(Token);
        Assert.Contains(listed, listedWindow => listedWindow.Id == window);

        // 恢复响应后同一编号还能拍快照
        _platform.UnresponsiveTitles.Remove("Note");
        DesktopView view = await _host.SnapshotAsync("s", window, Token);
        Assert.NotEmpty(view.Snapshot);
        Assert.Equal(window, view.Window.Id);
    }

    [Fact]
    public async Task AWindowThatReallyCloses_IsDroppedFromListing_AndItsSnapshotIsGone()
    {
        string window = await OpenNoteAsync();
        await _host.SnapshotAsync("s", window, Token);

        // 系统里没了（Fake 的窗口列表移除它），再列一次：从 _windows 清掉、快照连同原生句柄一起丢
        _platform.Windows.RemoveAll(info => info.Title == "Note");
        IReadOnlyList<DesktopWindow> listed = await _host.ListWindowsAsync(Token);

        Assert.Null(_host.FindWindow(window));
        Assert.DoesNotContain(listed, listedWindow => listedWindow.Id == window);
    }

    [Fact]
    public async Task Peek_TranslatesARefFromTheSessionsLatestSnapshot()
    {
        string window = await OpenNoteAsync();

        DesktopElementPreview? preview = _host.Peek("s", window, "e3");

        Assert.Equal(new DesktopElementPreview("TextEdit", "Note", "textbox", "Password", true), preview);
        Assert.Equal(string.Empty, _host.Peek("other session", window, "e3")!.Role);
    }

    [Fact]
    public async Task Unavailable_SaysWhy()
    {
        _platform.Unavailable = "Accessibility permission has not been granted.";

        AxActionException error = await Assert.ThrowsAsync<AxActionException>(() => _host.ListWindowsAsync(Token));

        Assert.Contains("permission", error.Message);
        Assert.Contains("not supported", new DesktopHost(null).Unavailable);
    }

    [Fact]
    public async Task Show_WaitsForTheUserToStop_BeforeRaisingTheWindow()
    {
        _host.ShowIdleWaitMs = 100; //测试调短
        string window = await OpenNoteAsync();

        _platform.SecondsSinceUserInput = 0.3; //用户正握着鼠标键盘
        Assert.False(_host.Show(window));
        Assert.Empty(_platform.Raised); //没抢前台，也没触发会真按 Alt 的那条路

        _platform.SecondsSinceUserInput = 3; //用户停手了
        Assert.True(_host.Show(window));
        Assert.Equal(["Note"], _platform.Raised);
    }

    [Fact]
    public async Task Show_RaisesTheWindow_WhenTheUserIsIdle()
    {
        string window = await OpenNoteAsync();
        _platform.SecondsSinceUserInput = 2;

        Assert.True(_host.Show(window));
        Assert.Equal(["Note"], _platform.Raised);
    }

    private async Task<string> OpenNoteAsync()
    {
        _platform.AddWindow("TextEdit", "com.apple.TextEdit", "Note", Form());
        await _host.ListWindowsAsync(Token);
        await _host.SnapshotAsync("s", "TextEdit", Token);
        return "w1";
    }
}
