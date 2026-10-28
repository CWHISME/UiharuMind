using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Features.ScreenCapture;

namespace UiharuMind.App.Tests.ScreenCapture;

/// <summary>
/// 停靠窗两套面板（浏览 / 编辑工具条）的切换。
/// <para>
/// 回归点：早先是「先给 MinWidth 把窗口撑到编辑条尺寸、等 SizeChanged 回灌再换面板」。
/// 但停靠窗是 <c>SizeToContent</c> 窗口，实测改 MinWidth 既不改变窗口尺寸、也不会标脏布局
/// （窗口尺寸不变、SizeChanged 一次都不来），于是面板永远停在浏览态——点「画图」什么反应都没有。
/// </para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ScreenCaptureDockPanelSwapTests
{
    [Fact]
    public void EditMode_SwapsToEditPanel_AndBack() => HeadlessUi.Run(() =>
    {
        using Bitmap source = new WriteableBitmap(new PixelSize(200, 120), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        var pin = new ScreenCapturePreviewWindow { ImageSource = source };
        var dock = new ProbeDock();
        try
        {
            pin.Show();
            dock.Show();
            dock.SetMainWindow(pin);
            dock.UpdateLayout();
            Assert.True(dock.BrowsePanel.IsVisible, "浏览态应显示浏览面板");

            pin.EnterEditMode();
            dock.UpdateLayout();

            Assert.True(pin.EditMode);
            Assert.False(dock.BrowsePanel.IsVisible, "进编辑态后浏览面板应收起");
            Assert.True(dock.EditToolbar.IsVisible, "进编辑态后应显示编辑工具条");
            Console.WriteLine($"[dock] client={dock.ClientSize} bounds={dock.Bounds} desired={dock.DesiredSize} " +
                              $"w={dock.Width} h={dock.Height} edit={dock.EditToolbar.DesiredSize} pill={dock.PillBorder.DesiredSize}");
            // 窗口尺寸定成编辑条的尺寸：定不到就是编辑条会被排进浏览面板的旧宽度里（右边全裁）
            Assert.True(dock.Width >= dock.EditToolbar.DesiredSize.Width,
                $"窗口宽 {dock.Width} 应容得下编辑条 {dock.EditToolbar.DesiredSize.Width}");
            double widthInEdit = dock.Width;

            pin.CancelEditMode();
            dock.UpdateLayout();

            Assert.True(dock.BrowsePanel.IsVisible, "退出编辑态后应回到浏览面板");
            Assert.False(dock.EditToolbar.IsVisible);
            Assert.True(dock.Width < widthInEdit, $"退出编辑后窗口应收窄（{widthInEdit} → {dock.Width}）");
        }
        finally
        {
            dock.Close();
            pin.Close();
        }
    });

    /// <summary>
    /// 不碰 <c>App.ScreensService</c>（无头测试里没有它的实例）的停靠窗：跟随定位那一步空转。
    /// </summary>
    private sealed class ProbeDock : ScreenCaptureDockWindow
    {
        protected override void OnFollowTarget(PixelPoint targetPosition, Rect anchor)
        {
        }
    }
}
