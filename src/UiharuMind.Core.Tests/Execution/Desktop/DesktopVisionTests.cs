using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;

namespace UiharuMind.Core.Tests.Execution.Desktop;

/// <summary>
/// 桌面的看图（ADR 0084）：树太空时认图里的字，认出的行带编号、点它就是点框的中心；要截图时把编号画到图上
/// </summary>
public class DesktopVisionTests
{
    private readonly FakeDesktopPlatform _platform = new();
    private readonly FakeVision _vision = new();
    private readonly DesktopHost _host;

    public DesktopVisionTests()
    {
        _host = new DesktopHost(_platform) { SettleDelayMs = 1, Vision = _vision };
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // 画布类窗口：无障碍树里只有窗口按钮
    private static AxNode Canvas() => new("window", "Game")
    {
        Children = [new AxNode("button", "close") { Source = "native" }],
    };

    private static AxNode Rich() => new("window", "Form")
    {
        Children = Enumerable.Range(1, 6).Select(i => new AxNode("button", $"Button {i}")
        {
            Bounds = new AxRect(100 + i * 50, 100, 40, 20), Source = "native",
        }).ToList(),
    };

    [Fact]
    public async Task AnEmptyTree_GetsTheTextReadFromThePicture_AndThoseLinesCanBeClicked()
    {
        _platform.AddWindow("Game", "game", "Game", Canvas(), new AxRect(100, 50, 800, 600));
        _vision.Lines.Add(new DesktopTextLine("Start", new AxRect(0.5, 0.5, 0.1, 0.05)));
        await _host.ListWindowsAsync(Token);

        DesktopView view = await _host.SnapshotAsync("s", "w1", Token);
        Assert.Contains("- ocr-text \"Start\" [ref=e2]: [col:1] at 440,315", view.Snapshot); //带列标注与中心位置

        await _host.ActAsync("s", new DesktopActionRequest("w1", EDesktopAction.Click, "e2", "", false), Token);
        DesktopInput click = Assert.Single(_platform.Inputs).Input;
        Assert.Equal((540d, 365d), (click.X, click.Y)); //窗口原点 + 框中心
        Assert.Empty(_platform.Performed); //没有无障碍元素可调，不走第 1 级
    }

    /// <summary>画出来的界面动作后树不变：比截图，画面变了又稳住就收，不等满</summary>
    [Fact]
    public async Task AfterActing_APaintedWindowWaitsForThePictureToChangeAndSettle()
    {
        _platform.AddWindow("Game", "game", "Game", Canvas());
        _vision.Lines.Add(new DesktopTextLine("Start", new AxRect(0.5, 0.5, 0.1, 0.05)));
        await _host.ListWindowsAsync(Token);
        await _host.SnapshotAsync("s", "w1", Token);
        Assert.Equal(1, _platform.Captures);

        _platform.NextPictures.Enqueue([1, 2, 3]); //页面还没换
        _platform.NextPictures.Enqueue([4]);
        _platform.NextPictures.Enqueue([5]);
        _platform.NextPictures.Enqueue([5]);
        _platform.Picture = [5];
        await _host.ActAsync("s", new DesktopActionRequest("w1", EDesktopAction.Click, "e2", "", false), Token);

        Assert.Equal(5, _platform.Captures);
    }

    [Fact]
    public async Task AfterActing_AnUnchangedPictureIsAcceptedAfterAFewReads()
    {
        _platform.AddWindow("Game", "game", "Game", Canvas());
        _vision.Lines.Add(new DesktopTextLine("Start", new AxRect(0.5, 0.5, 0.1, 0.05)));
        await _host.ListWindowsAsync(Token);
        await _host.SnapshotAsync("s", "w1", Token);

        await _host.ActAsync("s", new DesktopActionRequest("w1", EDesktopAction.Click, "e2", "", false), Token);

        Assert.Equal(1 + 5, _platform.Captures);
    }

    /// <summary>单个字符的行多是图标认错，丢掉；字母数字与箭头留着</summary>
    [Fact]
    public async Task SingleCharacterNoise_IsDropped()
    {
        _platform.AddWindow("Game", "game", "Game", Canvas());
        foreach (string text in new[] { "•", "贝", "¢", "<", "A", "Fab" })
        {
            _vision.Lines.Add(new DesktopTextLine(text, new AxRect(0.1, 0.1, 0.1, 0.05)));
        }
        await _host.ListWindowsAsync(Token);

        DesktopView view = await _host.SnapshotAsync("s", "w1", Token);

        Assert.DoesNotContain("\"•\"", view.Snapshot);
        Assert.DoesNotContain("\"贝\"", view.Snapshot);
        Assert.DoesNotContain("\"¢\"", view.Snapshot);
        Assert.Contains("ocr-text \"<\"", view.Snapshot);
        Assert.Contains("ocr-text \"A\"", view.Snapshot);
        Assert.Contains("ocr-text \"Fab\"", view.Snapshot);
    }

    [Fact]
    public async Task ARichTree_IsNotPhotographed()
    {
        _platform.AddWindow("Form", "form", "Form", Rich());
        await _host.ListWindowsAsync(Token);

        await _host.SnapshotAsync("s", "w1", Token);

        Assert.Equal(0, _platform.Captures);
    }

    [Fact]
    public async Task Screenshot_BoxesEveryVisibleRef()
    {
        _platform.AddWindow("Form", "form", "Form", Rich(), new AxRect(100, 50, 800, 600));
        await _host.ListWindowsAsync(Token);

        DesktopView view = await _host.SnapshotAsync("s", "w1", Token, EDesktopView.Screenshot);

        Assert.Equal(FakeVision.Marked, view.Picture);
        Assert.Equal(6, _vision.LastMarks.Count);
        Assert.Equal(new DesktopMark("e1", new AxRect(0.0625, 50 / 600d, 0.05, 20 / 600d)), _vision.LastMarks[0]);
    }

    [Fact]
    public async Task WithoutScreenRecording_AnEmptyTreeSaysWhyInsteadOfFailing()
    {
        _platform.AddWindow("Game", "game", "Game", Canvas());
        _platform.CaptureRefusal = "needs the Screen Recording permission";
        await _host.ListWindowsAsync(Token);

        DesktopView view = await _host.SnapshotAsync("s", "w1", Token);

        Assert.Contains("Screen Recording", Assert.Single(view.Notes));
        await Assert.ThrowsAsync<AxActionException>(() => _host.SnapshotAsync("s", "w1", Token, EDesktopView.Ocr));
    }

    private sealed class FakeVision : IDesktopVision
    {
        public static readonly byte[] Marked = [9, 9];

        public List<DesktopTextLine> Lines { get; } = new();

        public IReadOnlyList<DesktopMark> LastMarks { get; private set; } = [];

        public Task<IReadOnlyList<DesktopTextLine>> RecognizeAsync(byte[] png, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DesktopTextLine>>(Lines);

        public byte[] Mark(byte[] png, IReadOnlyList<DesktopMark> marks)
        {
            LastMarks = marks;
            return Marked;
        }
    }
}
