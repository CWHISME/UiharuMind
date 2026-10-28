using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Features.ScreenCapture;
using UiharuMind.Features.ScreenCapture.Drawing;

namespace UiharuMind.App.Tests.ScreenCapture;

/// <summary>
/// 贴图窗编辑模式的回归防线（随独立编辑窗删除，由原 ScreenCaptureEditWindowHeadlessTests 改写）。
/// <list type="bullet">
/// <item>编辑工具条按钮高度与宿主（停靠窗）尺寸无关——旧实现的按钮被 Star 剩余空间撑大，就是这里的回归</item>
/// <item>编辑模式导出的合成图尺寸恒等于原始像素（坐标零换算的兑现），取消不换图</item>
/// </list>
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ScreenCaptureEditModeHeadlessTests
{
    private static Bitmap CreateBitmap(int width, int height) =>
        new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    [Fact]
    public void Toolbar_StaysConstantHeight_UnderDifferentHostWidth() => HeadlessUi.Run(() =>
    {
        ToolbarHost small = OpenToolbarHost(240);
        ToolbarHost large = OpenToolbarHost(800);
        try
        {
            double smallHeight = small.Toolbar.GeometryRectangleButton.Bounds.Height;
            double largeHeight = large.Toolbar.GeometryRectangleButton.Bounds.Height;

            Assert.True(smallHeight > 0, "按钮应当有实际高度");
            Assert.Equal(smallHeight, largeHeight);
        }
        finally
        {
            small.Close();
            large.Close();
        }
    });

    [Fact]
    public void EditMode_RenderExports_AtOriginalPixelSize_AndCancelKeepsImage() => HeadlessUi.Run(() =>
    {
        Bitmap source = CreateBitmap(200, 120);
        var window = new ScreenCapturePreviewWindow();
        try
        {
            // 不经 SetImage（依赖 App.ScreensService），直接挂图验证编辑态状态机
            window.ImageSource = source;
            window.EnterEditMode();

            Assert.True(window.EditMode, "进入编辑模式后 EditMode 应为 true");
            Assert.True(window.EditEditor.IsVisible, "编辑器应叠加显示");
            // 没走 SetImage 时没有显示尺寸可依，视图退回 1:1（不得凭空放大）
            Assert.Equal(1.0, window.EditEditor.ViewScale, 3);

            using Bitmap output = window.EditEditor.RenderToBitmap();
            Assert.Equal(new PixelSize(200, 120), output.PixelSize);

            window.CancelEditMode();
            Assert.False(window.EditMode, "取消后应回到浏览模式");
            Assert.False(window.EditEditor.IsVisible, "取消后编辑器应隐藏");
            Assert.Same(source, window.ImageSource); // 取消不换图
        }
        finally
        {
            window.Close(); // OnClosed 释放 ImageSource
        }
    });

    /// <summary>
    /// 编辑器画布是<b>位图像素</b>口径，初始视图缩放必须按「显示宽 / 位图宽」给。
    /// 回归点：曾经直接把宿主那套 DIP 之间的比率（显示尺寸/原始尺寸）传进来，
    /// 125% 屏上编辑视图就比贴图大 1.25 倍——「点编辑，图被放大一圈，显示不全」。
    /// </summary>
    [Fact]
    public void ScaleForDisplay_MapsDisplayDipsToBitmapPixels() => HeadlessUi.Run(() =>
    {
        Bitmap source = CreateBitmap(200, 120);
        try
        {
            // 同一张 200px 宽的图，在 125% 屏上贴图显示成 250 DIP → 视图 1.25（不是 1.0）
            Assert.Equal(1.25, ImageAnnotationEditor.ScaleForDisplay(source, new Size(250, 150)), 3);
            // 显示缩到一半
            Assert.Equal(0.5, ImageAnnotationEditor.ScaleForDisplay(source, new Size(100, 60)), 3);
            // 尺寸不可用时退回 1:1，不能算出 0（视图会整张消失）
            Assert.Equal(1.0, ImageAnnotationEditor.ScaleForDisplay(source, default), 3);
        }
        finally
        {
            source.Dispose();
        }
    });

    [Fact]
    public void EditMode_RenderExports_AtOriginalPixelSize_RegardlessOfZoom() => HeadlessUi.Run(() =>
    {
        Bitmap source = CreateBitmap(200, 120);
        try
        {
            // 初始视图缩放只影响显示变换，不影响导出尺寸（RenderToBitmap 恒按原始像素渲染）
            var editor = new ImageAnnotationEditor();
            editor.SetSource(source, 4.0);
            using Bitmap output = editor.RenderToBitmap();
            Assert.Equal(new PixelSize(200, 120), output.PixelSize);
        }
        finally
        {
            source.Dispose();
        }
    });

    private static ToolbarHost OpenToolbarHost(double width)
    {
        ToolbarHost host = new(width);
        host.Show();
        host.UpdateLayout();
        return host;
    }

    private sealed class ToolbarHost : Window
    {
        public ScreenCaptureEditToolbar Toolbar { get; }

        public ToolbarHost(double width)
        {
            Width = width;
            Height = 64;
            Toolbar = new ScreenCaptureEditToolbar();
            Content = Toolbar;
        }
    }
}
