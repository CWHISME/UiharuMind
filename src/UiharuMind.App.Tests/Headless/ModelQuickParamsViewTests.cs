using Avalonia.Controls;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Features.Models;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 对话页模型栏的快捷参数：远程模型与没选模型只给说明，不出参数行与「全部恢复」
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ModelQuickParamsViewTests
{
    [Fact]
    public void RemoteAndNoModel_ShowOnlyExplanation()
    {
        HeadlessUi.Run(() =>
        {
            ModelQuickParamsView view = new();
            Window window = new() { Width = 560, Height = 300, Content = view };
            window.Show();

            view.Show(new ModelRunningData(new RemoteModelInfo { Config = { ModelName = "remote-x" } }));
            Assert.Equal("remote-x", view.FindControl<TextBlock>("ModelNameText")!.Text);
            Assert.Equal(Loc.Text(LangKey.ModelParamsRemoteDesc), view.FindControl<TextBlock>("DescText")!.Text);
            Assert.False(view.FindControl<ModelRuntimeOverridesView>("Rows")!.IsVisible);
            Assert.False(view.FindControl<Button>("ResetAllButton")!.IsVisible);

            view.Show(null);
            Assert.Equal(Loc.Text(LangKey.ModelParamsNoModel), view.FindControl<TextBlock>("ModelNameText")!.Text);
            window.Close();
        });
    }
}
