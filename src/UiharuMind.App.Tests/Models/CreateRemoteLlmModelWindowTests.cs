using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 创建远程模型对话框：模型 ID 必填 + 复制已有模型要带上模型 ID。
/// </summary>
public class CreateRemoteLlmModelWindowTests
{
    private static RemoteModelInfo CreateSource(
        string name = "source-model",
        string modelId = "deepseek-v4-flash")
    {
        return new RemoteModelInfo
        {
            Config = new RemoteDeepSeekModelConfig
            {
                ModelName = name,
                ModelId = modelId,
                ContextLength = 32768,
            },
            ApiKey = "test-key",
        };
    }

    private static RemoteModelEditViewData CreateViewModelFor(Type configType)
    {
        var vm = new RemoteModelEditViewData();
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == configType);
        return vm;
    }

    /// <summary>不填模型 ID 也能确认：空 ID 的模型建出来调接口必挂</summary>
    [Fact]
    public void CreateMode_EmptyModelId_CannotConfirm()
    {
        RemoteModelEditViewData vm = CreateViewModelFor(typeof(RemoteDeepSeekModelConfig));
        vm.ModelName = "new-model-" + Guid.NewGuid().ToString("N");
        vm.ApiKey = "test-key";
        vm.ModelId = "";

        Assert.True(vm.HasModelIdError);
        Assert.False(vm.CanConfirm);
    }

    /// <summary>复制已有模型：模型 ID 要一块带过去</summary>
    [Fact]
    public void CopySource_BringsModelIdOver()
    {
        RemoteModelInfo source = CreateSource();
        RemoteModelEditViewData vm = CreateViewModelFor(typeof(RemoteDeepSeekModelConfig));
        vm.SelectCopySourceCommand.Execute(source);

        Assert.Equal(source.Config.ModelId, vm.ModelId);
        Assert.Equal(source.Config.ModelId, vm.SelectedModelIdOption?.Id);
        Assert.Equal(source.Config.ModelId, vm.BuildResult().Config.ModelId);
    }
}
