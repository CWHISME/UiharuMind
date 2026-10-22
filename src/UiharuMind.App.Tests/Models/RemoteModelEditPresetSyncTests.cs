using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 创建远程模型时，与模型强相关的选项（视觉、不发送采样参数、上下文、输出预算）要跟随 ModelId 的预设自动切换。
/// 这里钉住当前 VM 的实际行为，作为改造的基线。
/// </summary>
public class RemoteModelEditPresetSyncTests
{
    private static RemoteModelEditViewData CreateViewModelFor(Type configType)
    {
        var vm = new RemoteModelEditViewData();
        vm.SelectedProvider = vm.Providers.First(p => p.ConfigType == configType);
        return vm;
    }

    /// <summary>切服务商后，默认模型 ID 的预设（视觉/上下文/预算）随之下发到表单，而不是留在默认值</summary>
    [Fact]
    public void ProviderSwitch_PrefillsDefaultModelPreset()
    {
        RemoteModelEditViewData vm = CreateViewModelFor(typeof(RemoteDeepSeekModelConfig));

        // DeepSeek 服务商默认模型 deepseek-flash 的预设：视觉、ContextLength 1048576、MaxTokens 393216
        Assert.Equal("deepseek-flash", vm.ModelId);
        Assert.True(vm.IsVision);
        Assert.Equal("1048576", vm.ContextLengthText);
        Assert.Equal("393216", vm.MaxTokensText);
    }

    /// <summary>下拉切换到另一条模型：视觉等强相关选项跟着预设走（视觉模型开、文本模型关）</summary>
    [Fact]
    public void DropdownSwitch_AppliesPresetTraits()
    {
        RemoteModelEditViewData vm = CreateViewModelFor(typeof(RemoteZhipuModelConfig));

        // glm-5v-turbo 是视觉模型
        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "glm-5v-turbo");
        Assert.True(vm.IsVision);
        Assert.Equal("204800", vm.ContextLengthText);
        Assert.Equal("131072", vm.MaxTokensText);

        // 切到文本模型 glm-5.3：视觉应自动关掉
        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "glm-5.3");
        Assert.False(vm.IsVision);
        Assert.Equal("1048576", vm.ContextLengthText);
        Assert.Equal("131072", vm.MaxTokensText);
    }

    /// <summary>手输/粘贴一个在预设表里的 ID：命中预设时视觉等强相关选项也自动跟随（与下拉选择同一口径）</summary>
    [Fact]
    public void TypedId_MatchingPreset_AppliesTraits()
    {
        RemoteModelEditViewData vm = CreateViewModelFor(typeof(RemoteZhipuModelConfig));
        // 默认 glm-4.7-flash（文本模型），先手动把视觉改开，模拟已经拨过开关的状态
        vm.IsVision = true;

        vm.ModelId = "glm-5v-turbo";

        Assert.Equal("glm-5v-turbo", vm.SelectedModelIdOption?.Id); // 选中项对齐了
        Assert.True(vm.IsVision); // 视觉跟随预设（仍然是开——glm-5v-turbo 就是视觉模型）
        Assert.Equal("204800", vm.ContextLengthText);
        Assert.Equal("131072", vm.MaxTokensText);
    }

    /// <summary>手输预设表之外的自定义 ID：不应用任何预设，保留手头的值</summary>
    [Fact]
    public void TypedId_Custom_DoesNotApplyTraits()
    {
        RemoteModelEditViewData vm = CreateViewModelFor(typeof(RemoteZhipuModelConfig));
        vm.IsVision = true;

        vm.ModelId = "my-custom-id";

        Assert.Null(vm.SelectedModelIdOption);
        Assert.True(vm.IsVision); // 自定义 ID 不该被预设覆盖
    }

    /// <summary>编辑模式初始化：已保存的手动设置原样带回，不被选中模型的预设覆盖（预填只属于创建流程与主动切换）</summary>
    [Fact]
    public void EditMode_Init_KeepsUserValuesOverPreset()
    {
        var source = new RemoteModelInfo
        {
            Config = new RemoteZhipuModelConfig
            {
                ModelName = "edited-model",
                ModelId = "glm-5.3", // 该模型预设 ContextLength=1048576 / MaxTokens=131072 / IsVision=false
                ContextLength = 32768, // 用户手动改过
                MaxTokens = 5000, // 用户手动改过
                IsVision = true, // 用户手动开过
                RequiresReasoningContentRoundtripOverride = true, // 用户手动覆盖过
            },
            ApiKey = "key",
        };
        var vm = new RemoteModelEditViewData(source, _ => false);

        Assert.Equal("32768", vm.ContextLengthText); // 保留用户值，不被预设 1048576 覆盖
        Assert.Equal("5000", vm.MaxTokensText);
        Assert.True(vm.IsVision);
        Assert.True(vm.RequiresReasoningContentRoundtripOverride);
    }
}