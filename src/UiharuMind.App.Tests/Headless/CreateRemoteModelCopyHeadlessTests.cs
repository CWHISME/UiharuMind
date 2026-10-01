using Avalonia.Controls;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 复制已有模型时，模型 ID 输入框（真实模板里的可编辑下拉框）要显示源模型的 ID。
/// VM 层的值复制是对的（见 <c>CreateRemoteLlmModelWindowTests</c>），这里钉的是用户实际看到的那一格。
/// </summary>
[Collection(HeadlessCollection.Name)]
public class CreateRemoteModelCopyHeadlessTests
{
    [Fact]
    public void CopySource_ModelIdBoxShowsSourceId()
    {
        HeadlessUi.Run(() =>
        {
            var source = new RemoteModelInfo
            {
                Config = new RemoteDeepSeekModelConfig
                {
                    ModelName = "source-model",
                    ModelId = "deepseek-v4-flash",
                },
                ApiKey = "test-key",
            };
            var vm = new RemoteModelEditViewData();
            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteDeepSeekModelConfig));
            var window = new CreateRemoteLlmModelWindow { DataContext = vm };
            window.Show();

            ComboBox idBox = window.GetVisualDescendants()
                .OfType<ComboBox>()
                .First(b => ReferenceEquals(b.ItemsSource, vm.ModelIdOptions));

            Assert.Equal("deepseek-flash", vm.ModelId);
            Assert.Equal("deepseek-flash", idBox.Text);
            vm.SelectCopySourceCommand.Execute(source);

            Assert.Equal("deepseek-v4-flash", vm.ModelId);
            Assert.Equal("deepseek-v4-flash", idBox.Text);
            window.Close();
        });
    }

    /// <summary>下拉点选另一条：文本与 VM 同步跟过去，确认按钮放行（ID 非空）</summary>
    [Fact]
    public void PickFromDropdown_SyncsModelIdText()
    {
        HeadlessUi.Run(() =>
        {
            var vm = new RemoteModelEditViewData();
            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteDeepSeekModelConfig));
            var window = new CreateRemoteLlmModelWindow { DataContext = vm };
            window.Show();

            ComboBox idBox = window.GetVisualDescendants()
                .OfType<ComboBox>()
                .First(b => ReferenceEquals(b.ItemsSource, vm.ModelIdOptions));

            vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "deepseek-v4-pro");

            Assert.Equal("deepseek-v4-pro", vm.ModelId);
            Assert.Equal("deepseek-v4-pro", idBox.Text);
            window.Close();
        });
    }

    /// <summary>手输预设表之外的自定义 ID：VM 留得住，不被选中项回写冲掉</summary>
    [Fact]
    public void CustomTypedId_SurvivesSelectionSync()
    {
        HeadlessUi.Run(() =>
        {
            var vm = new RemoteModelEditViewData();
            vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == typeof(RemoteDeepSeekModelConfig));
            var window = new CreateRemoteLlmModelWindow { DataContext = vm };
            window.Show();

            ComboBox idBox = window.GetVisualDescendants()
                .OfType<ComboBox>()
                .First(b => ReferenceEquals(b.ItemsSource, vm.ModelIdOptions));

            idBox.Text = "my-custom-id";

            Assert.Equal("my-custom-id", vm.ModelId);
            Assert.Equal("my-custom-id", idBox.Text);
            window.Close();
        });
    }
}
