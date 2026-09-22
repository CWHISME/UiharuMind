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
public class QuickToolWindowTests
{
    private sealed class TestableQuickToolWindow : QuickToolWindow
    {
        public void Expand() => PlayAnimation(true);

        public void Collapse() => PlayAnimation(false);
    }

    [Fact]
    public void InitialState_MenuCollapsedAndFourItems()
    {
        HeadlessUi.Run(() =>
        {
            var window = new TestableQuickToolWindow();
            window.Awake();
            window.Show();
            window.UpdateLayout();

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
            var window = new TestableQuickToolWindow();
            window.Awake();
            window.Show();
            window.UpdateLayout();

            double collapsedWidth = window.DesiredSize.Width;
            double collapsedMenuWidth = window.MainMenu.DesiredSize.Width;
            window.Expand();
            window.UpdateLayout();

            // 无头窗口的 Bounds 不跟随 SizeToContent（无头不真实重排），用 DesiredSize 量布局期望
            Assert.True(window.MainMenu.IsVisible);
            Assert.True(window.MainMenu.IsHitTestVisible);
            Assert.Equal(0, collapsedMenuWidth);            // 折叠时菜单不占布局（幽灵区消失）
            Assert.True(window.MainMenu.DesiredSize.Width > 0);  // 展开后菜单恢复参与布局
            // 窗口宽度现在是手动动画（SizeToContent=Height），窗口 DesiredSize 不再反映内容；
            // 用卡片内容期望宽度验证「向右扩」（这正是展开目标宽度的来源）
            window.Card.Measure(Avalonia.Size.Infinity);
            Assert.True(window.Card.DesiredSize.Width > collapsedWidth);
        });
    }

    [Fact]
    public void Collapse_WhenAlreadyCollapsed_DoesNothing()
    {
        HeadlessUi.Run(() =>
        {
            var window = new TestableQuickToolWindow();
            window.Awake();
            window.Show();
            window.UpdateLayout();

            // 已折叠时收起：直接跳过动画，菜单保持折叠
            window.Collapse();
            Assert.False(window.MainMenu.IsVisible);
        });
    }
}
