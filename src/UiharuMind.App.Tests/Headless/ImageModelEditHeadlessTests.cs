using Avalonia.Controls;
using Avalonia.VisualTree;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Features.Models.ImageModels;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 生图模型编辑窗的模型 id 下拉框（真实模板）：换预设、点候选、手输自定义 id，用户看到的那一格都要对。
/// 可编辑下拉框的 Text 与 SelectedItem 两条双向绑定互相回写，文本模型窗口在这里丢过 id
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ImageModelEditHeadlessTests
{
    private static (ImageModelEditWindow Window, ImageModelEditViewData Form, ComboBox IdBox) Open()
    {
        ImageModelEditViewData form = new(null, [], []);
        ImageModelEditWindow window = new() { DataContext = form };
        window.Show();
        ComboBox idBox = window.GetVisualDescendants()
            .OfType<ComboBox>()
            .First(box => ReferenceEquals(box.ItemsSource, form.ModelIdOptions));
        return (window, form, idBox);
    }

    [Fact]
    public void SwitchingPreset_ShowsTheNewDefaultId() => HeadlessUi.Run(() =>
    {
        var (window, form, idBox) = Open();
        Assert.Equal("sensenova-u1.5-lite", idBox.Text);

        form.SelectedPreset = form.Presets.Single(p => p.Preset.Key == "agnes");

        Assert.Equal("agnes-image-2.5-flash", form.ModelId);
        Assert.Equal("agnes-image-2.5-flash", idBox.Text);
        window.Close();
    });

    [Fact]
    public void PickingAnOption_SyncsTheText() => HeadlessUi.Run(() =>
    {
        var (window, form, idBox) = Open();

        form.SelectedModelIdOption = "sensenova-u1.5-fast";

        Assert.Equal("sensenova-u1.5-fast", idBox.Text);
        Assert.Equal("sensenova-u1.5-fast", form.Name); //名字仍跟着 id 走
        window.Close();
    });

    [Fact]
    public void TypedCustomId_SurvivesTheSelectionSync() => HeadlessUi.Run(() =>
    {
        var (window, form, idBox) = Open();

        idBox.Text = "sensenova-u2-preview";

        Assert.Equal("sensenova-u2-preview", form.ModelId);
        Assert.Equal("sensenova-u2-preview", idBox.Text);
        Assert.Equal(EImageDialect.SenseNova, form.Dialect);
        window.Close();
    });
}
