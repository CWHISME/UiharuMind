using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 创建/编辑远程模型对话框的供应商列表形状等价：由显式注册表驱动后，
/// 列表仍是四家供应商（固定顺序）+ 通用自定义配置，且每项都有可显示的文案。
/// </summary>
public class RemoteModelProviderListTests
{
    [Fact]
    public void Providers_AreFixedVendorListPlusCustomConfig()
    {
        var vm = new RemoteModelEditViewData();

        Assert.Equal(8, vm.Providers.Count);
        Assert.Equal(typeof(RemoteAgnesModelConfig), vm.Providers[0].ConfigType);
        Assert.Equal(typeof(RemoteDeepSeekModelConfig), vm.Providers[1].ConfigType);
        Assert.Equal(typeof(RemoteSensenovaModelConfig), vm.Providers[2].ConfigType);
        Assert.Equal(typeof(RemoteZhipuModelConfig), vm.Providers[3].ConfigType);
        Assert.Equal(typeof(RemoteCommandCodeModelConfig), vm.Providers[4].ConfigType);
        Assert.Equal(typeof(RemoteOpenCodeModelConfig), vm.Providers[5].ConfigType);
        Assert.Equal(typeof(RemoteOpenRouterModelConfig), vm.Providers[6].ConfigType);
        Assert.Equal(typeof(RemoteModelConfig), vm.Providers[7].ConfigType);
    }

    [Fact]
    public void VendorProviders_HaveDisplayNameAndDefaultEndpoint()
    {
        var vm = new RemoteModelEditViewData();

        foreach (ProviderItem provider in vm.Providers.Take(7))
        {
            Assert.False(string.IsNullOrEmpty(provider.Name));
            Assert.False(string.IsNullOrEmpty(provider.DefaultEndpoint));
        }
    }
}
