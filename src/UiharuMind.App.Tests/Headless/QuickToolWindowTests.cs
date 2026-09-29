using System;
using UiharuMind.Features.QuickTools;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// QuickToolWindow 的状态机回归。
///
/// 重点守住「幽灵区」这一条：菜单初始必须折叠、不占布局，否则窗口宽度里就藏着一片
/// 看不见的命中区（鼠标挪进去不反应、窗口也不走）。
///
/// 动画的<b>最终态</b>（收起动画播完才重新折叠）依赖真实时钟：无头同步体里等不到
/// Task.Delay 的续体，这部分留给真机验证，这里只断言同步可达的状态转移。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class QuickToolWindowTests : IDisposable
{
    private readonly List<TestableQuickToolWindow> _windows = [];

    // 窗口不关的话，它的动画循环会一直留在同一进程里，拖累后面用到动画时钟的测试（曾让 PopupRevealTests 偶发失败）
    public void Dispose() => HeadlessUi.Run(() =>
    {
        foreach (TestableQuickToolWindow window in _windows) window.Close();
    });

    // 默认走「窗口随动画改尺寸」的路径（非 macOS 平台的行为）；fixedWidth 走 macOS 的固定宽度路径
    private TestableQuickToolWindow NewShownWindow(bool fixedWidth = false)
    {
        var window = new TestableQuickToolWindow(fixedWidth);
        _windows.Add(window);
        window.Awake();
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private sealed class TestableQuickToolWindow : QuickToolWindow
    {
        private readonly bool _fixedWidth;

        public TestableQuickToolWindow(bool fixedWidth) => _fixedWidth = fixedWidth;

        protected override bool UsesFixedWindowWidth => _fixedWidth;

        public void Expand() => PlayAnimation(true);

        public void Collapse() => PlayAnimation(false);
    }

    [Fact]
    public void InitialState_MenuCollapsedAndFourItems()
    {
        HeadlessUi.Run(() =>
        {
            var window = NewShownWindow();

            // 幽灵区修复：菜单初始不参与布局（窗口只有主按钮宽），也不可命中
            Assert.False(window.MainMenu.IsVisible);
            Assert.False(window.MainMenu.IsHitTestVisible);
            Assert.Equal(4, window.FunctionMenu.Children.Count);
        });
    }

    [Fact]
    public void Expand_MenuBecomesVisibleAndWindowGrows()
    {
        HeadlessUi.Run(() =>
        {
            var window = NewShownWindow();

            double collapsedWidth = window.DesiredSize.Width;
            double collapsedMenuWidth = window.MainMenu.DesiredSize.Width;
            window.Expand();
            window.UpdateLayout();

            // 无头窗口的 Bounds 不跟随 SizeToContent（无头不真实重排），用 DesiredSize 量布局期望
            Assert.True(window.MainMenu.IsVisible);
            Assert.True(window.MainMenu.IsHitTestVisible);
            Assert.Equal(0, collapsedMenuWidth);            // 折叠时菜单不占布局（幽灵区消失）
            Assert.True(window.MainMenu.DesiredSize.Width > 0);  // 展开后菜单恢复参与布局
            // 窗口宽度一步放到展开目标（只 resize 一次，逐帧动的是卡片宽度，见 PlayMenuSlideCoreAsync），
            // 所以同步部分跑完窗口就已经比折叠时宽
            Assert.True(window.Width > collapsedWidth);
        });
    }

    // Avalonia 会在平台上报尺寸不同时把 Window.Width 回写成上报值（Window.HandleResized），
    // 展开途中窗口宽度曾被悄悄改回折叠宽度，卡片随后取到被污染的宽度，成了细条。
    // 无头环境下第一次展开就能稳定复现这个回写，所以这条测试守的是「动画期间窗口宽度被恢复」
    [Fact]
    public void ExpandThenLeaveMidway_WindowStaysWideUntilFinished_CardNeverThin()
    {
        HeadlessUi.Run(() =>
        {
            var window = NewShownWindow();
            double collapsedWidth = window.Card.Bounds.Width;

            var samples = new List<(double Window, double Card, bool MenuVisible)>();
            void Watch(int milliseconds)
            {
                DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
                do
                {
                    PumpFrame();
                    Thread.Sleep(8);
                    samples.Add((window.Width, window.Card.Width, window.MainMenu.IsVisible));
                } while (DateTime.UtcNow < end);
            }

            window.Expand();
            Watch(60);
            window.Collapse(); // 展开播到一半，鼠标立刻离开
            Watch(500);

            double expandedWidth = samples.Max(x => x.Window);
            Assert.True(expandedWidth > collapsedWidth + 50, "测试前提：窗口确实展开过");
            // 只要菜单还在布局里(动画未结束)，窗口就必须保持展开宽度，不能被回写成别的值
            Assert.All(samples.Where(x => x.MenuVisible), x => Assert.Equal(expandedWidth, x.Window, 0.5));
            Assert.All(samples.Where(x => !double.IsNaN(x.Card)), x => Assert.True(x.Card >= collapsedWidth - 0.5,
                $"卡片被压到 {x.Card}，比折叠宽度 {collapsedWidth} 还窄"));
            Assert.False(window.MainMenu.IsVisible);
            Assert.Equal(collapsedWidth, window.Width, 0.5);
            Assert.True(double.IsNaN(window.Card.Width));
        });
    }

    [Fact]
    public void RapidHover_NeverLeavesCardThinAndSettlesBackToCollapsed()
    {
        HeadlessUi.Run(() =>
        {
            var window = NewShownWindow();
            double collapsedWidth = window.Card.Bounds.Width;

            // 模拟不断晃动鼠标：进入/离开来回打断，间隔比动画时长(150ms)短得多
            var random = new Random(42);
            double minCard = double.MaxValue;
            double minWindow = double.MaxValue;
            for (int i = 0; i < 300; i++)
            {
                if (random.Next(2) == 0) window.Expand();
                else window.Collapse();
                Pump(random.Next(0, 40));
                if (!double.IsNaN(window.Card.Width)) minCard = Math.Min(minCard, window.Card.Width);
                if (!double.IsNaN(window.Width)) minWindow = Math.Min(minWindow, window.Width);
            }

            window.Collapse();
            Pump(600);

            Assert.True(minCard >= collapsedWidth - 0.5, $"卡片被动画压到了 {minCard}，比折叠宽度 {collapsedWidth} 还窄");
            Assert.True(minWindow >= collapsedWidth - 0.5, $"窗口被压到了 {minWindow}，比折叠宽度 {collapsedWidth} 还窄");
            Assert.False(window.MainMenu.IsVisible);
            Assert.True(double.IsNaN(window.Card.Width), $"收起后卡片宽度应交还给布局，实际 {window.Card.Width}");
            Assert.Equal(collapsedWidth, window.Width, 0.5);
        });
    }

    private static void Pump(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            PumpFrame();
            Thread.Sleep(2);
        } while (DateTime.UtcNow < end);
    }

    // 动画跟显示帧走(见 UiAnimationUtils.RunFrameAnimationAsync)，无头环境里要自己推进渲染时钟
    private static void PumpFrame()
    {
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // macOS 路径：悬停期间原生窗口尺寸一次都不能变，否则 Avalonia 的 Metal 渲染会在尺寸变化后偶尔停摆
    [Fact]
    public void FixedWidth_WindowWidthNeverChangesWhileHovering()
    {
        HeadlessUi.Run(() =>
        {
            var window = NewShownWindow(fixedWidth: true);
            double initialWidth = window.Width;
            double collapsedCard = window.Card.Bounds.Width;
            Assert.Equal(480, initialWidth);

            var random = new Random(7);
            double maxCard = 0;
            for (int i = 0; i < 200; i++)
            {
                if (random.Next(2) == 0) window.Expand();
                else window.Collapse();
                Pump(random.Next(0, 40));
                Assert.Equal(initialWidth, window.Width, 0.5);
                if (!double.IsNaN(window.Card.Width)) maxCard = Math.Max(maxCard, window.Card.Width);
            }

            window.Collapse();
            Pump(600);

            Assert.True(maxCard > collapsedCard + 50, "测试前提：卡片确实展开过");
            Assert.Equal(initialWidth, window.Width, 0.5);
            Assert.False(window.MainMenu.IsVisible);
            Assert.True(double.IsNaN(window.Card.Width));
            Assert.Equal(collapsedCard, window.Card.Bounds.Width, 0.5);
        });
    }

    [Fact]
    public void FixedWidth_ExpandFillsCardWithoutResizingWindow()
    {
        HeadlessUi.Run(() =>
        {
            var window = NewShownWindow(fixedWidth: true);
            double collapsedCard = window.Card.Bounds.Width;

            window.Expand();
            Pump(400);

            Assert.True(window.MainMenu.IsVisible);
            Assert.True(window.Card.Bounds.Width > collapsedCard + 50);
            Assert.Equal(480, window.Width, 0.5);
        });
    }

    [Fact]
    public void Card_ClipsMenuContent()
    {
        HeadlessUi.Run(() =>
        {
            var window = NewShownWindow();

            // 收起时窗口保持宽度、只收窄卡片；不裁的话菜单里的文字和图标会悬在卡片外面
            Assert.True(window.Card.ClipToBounds);
        });
    }

    [Fact]
    public void ClipToBounds_AlsoClipsSvgIconsAndText()
    {
        HeadlessUi.Run(() =>
        {
            // 旧注释说 Svg 的绘制绕过合成层(父级 Opacity 对它不生效)，所以单独验证：
            // 裁剪对 Svg 图标和文字是否同样有效。窗口截图，看窄 Border 之外有没有内容画出来
            var icon = new UiharuMind.Shared.Controls.ThemedSvgIcon
            {
                IconName = "book-search", Width = 30, Height = 30,
                CurrentColor = Avalonia.Media.Colors.White,
            };
            var overflowing = new Avalonia.Controls.StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children =
                {
                    new Avalonia.Controls.Border { Width = 100 },
                    icon,
                    new Avalonia.Controls.TextBlock { Text = "WWWWWW", Foreground = Avalonia.Media.Brushes.White },
                },
            };
            var card = new Avalonia.Controls.Border
            {
                Width = 44, Height = 40, ClipToBounds = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Child = overflowing,
            };
            var window = new Avalonia.Controls.Window
            {
                Width = 300, Height = 40, Background = Avalonia.Media.Brushes.Black, Content = card,
            };
            window.Show();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!;

            Assert.True(frame.PixelSize.Width >= 250, "测试前提：窗口要比卡片宽得多");
            Assert.True(icon.Bounds.Right > 100, "测试前提：图标确实伸出了卡片");
            Assert.Equal(0, CountPixelsDifferentFromReference(frame, 50, frame.PixelSize.Width, frame.PixelSize.Width - 1));
        });
    }

    private static int CountPixelsDifferentFromReference(Avalonia.Media.Imaging.Bitmap frame, int fromX,
        int toX, int referenceX)
    {
        int width = frame.PixelSize.Width;
        int height = frame.PixelSize.Height;
        byte[] pixels = new byte[width * height * 4];
        System.Runtime.InteropServices.GCHandle handle =
            System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new Avalonia.PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        int different = 0;
        for (int y = 0; y < height; y++)
        {
            int referenceOffset = (y * width + referenceX) * 4;
            for (int x = fromX; x < toX; x++)
            {
                int offset = (y * width + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    if (pixels[offset + c] != pixels[referenceOffset + c])
                    {
                        different++;
                        break;
                    }
                }
            }
        }

        return different;
    }

    [Fact]
    public void Collapse_WhenAlreadyCollapsed_DoesNothing()
    {
        HeadlessUi.Run(() =>
        {
            var window = NewShownWindow();

            // 已折叠时收起：直接跳过动画，菜单保持折叠
            window.Collapse();
            Assert.False(window.MainMenu.IsVisible);
        });
    }
}
