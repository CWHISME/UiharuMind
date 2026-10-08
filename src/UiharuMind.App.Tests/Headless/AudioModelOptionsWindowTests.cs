using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.Audio;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Models.AudioModels;
using UiharuMind.Features.Models.AudioModels.Options;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 参数窗按值类型取到各自的编辑控件，展开「更多」后其余参数才出来
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class AudioModelOptionsWindowTests
{
    [Fact]
    public void EachKind_GetsItsEditor_MoreIsCollapsedUntilAsked() => HeadlessUi.Run(() =>
    {
        AudioCppSettingConfig settings = new();
        AudioModelItemData model = new(new AudioCppModelEntry("voxcpm1_0_5b_q8_0", "voxcpm1", "/m/v.gguf", "tts"), null, 0, settings);
        AudioModelOptionsViewData data = new(model, AudioModelOptionsTests.Options, settings, () => Task.CompletedTask);
        AudioModelOptionsWindow window = new() { DataContext = data };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, Visible<NumericUpDown>(window));
        Assert.Equal(0, Visible<ToggleSwitch>(window));

        data.ExpandMoreCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Equal(1, Visible<ToggleSwitch>(window));
        Assert.Equal(1, Visible<ComboBox>(window));
        Assert.Equal(1, Visible<TextBox>(window, x => x.FindAncestorOfType<NumericUpDown>() == null));
        Capture(window, "audio-model-options");
        window.Close();
    });

    private static int Visible<T>(Window window, Func<T, bool>? filter = null) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Count(x => x.IsEffectivelyVisible && (filter?.Invoke(x) ?? true));

    private static void Capture(Window window, string name)
    {
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        window.UpdateLayout();
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
                             ?? throw new InvalidOperationException("空帧");
        frame.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
