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

    /// <summary>新建时与已有模型同名不许确认：模型按名字认，重名会把那一个顶掉</summary>
    [Fact]
    public void CreateMode_TakenName_IsDuplicate()
    {
        var vm = new RemoteModelEditViewData(null, name => name == "taken-model") { ModelName = "  taken-model " };

        Assert.True(vm.HasDuplicateNameError);
        Assert.False(vm.CanConfirm);
    }

    /// <summary>编辑时保留原名放行：它占着的正是自己这个名字</summary>
    [Fact]
    public void EditMode_KeepingOriginalName_IsNotDuplicate()
    {
        var vm = new RemoteModelEditViewData(CreateSource("taken-model"), _ => true);

        Assert.False(vm.HasDuplicateNameError);

        vm.ModelName = "other-taken-model";
        Assert.True(vm.HasDuplicateNameError);
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

    /// <summary>复制名是占位（xxx-copy）：再选模型 id 时名字跟随，不被占位顶住</summary>
    [Fact]
    public void CopySource_ThenPickModelId_SyncsName()
    {
        RemoteModelInfo source = CreateSource();
        RemoteModelEditViewData vm = CreateViewModelFor(typeof(RemoteDeepSeekModelConfig));
        vm.SelectCopySourceCommand.Execute(source);

        Assert.Equal("source-model-copy", vm.ModelName);

        vm.SelectedModelIdOption = vm.ModelIdOptions.First(o => o.Id == "deepseek-v4-pro");

        Assert.Equal("deepseek-v4-pro", vm.ModelName);
    }

    /// <summary>编辑模式改了请求地址：旧的思考字段观测值作废，保存时清空（ADR 0093）</summary>
    [Fact]
    public void EditMode_ChangedModelPath_ClearsObservedThinkingField()
    {
        var source = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig
            {
                ModelName = "sensenova",
                ModelId = "sensenova-6.8-flash-lite",
                ModelPath = "https://old.example/v1/chat/completions",
                ObservedThinkingFields = { ["sensenova-6.8-flash-lite"] = "reasoning" }, // 上次观测落盘的值
            },
            ApiKey = "key",
        };
        var vm = new RemoteModelEditViewData(source, _ => false);
        vm.ModelPath = "https://new.example/v1/chat/completions";

        RemoteModelInfo result = vm.BuildResult();

        Assert.Empty(result.Config.ObservedThinkingFields);
    }

    /// <summary>编辑模式不改请求地址：观测值保留（只作响应侧记录，不决定请求键名）</summary>
    [Fact]
    public void EditMode_SameModelPath_KeepsObservedThinkingField()
    {
        var source = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig
            {
                ModelName = "sensenova",
                ModelId = "sensenova-6.8-flash-lite",
                ModelPath = "https://old.example/v1/chat/completions",
                ObservedThinkingFields = { ["sensenova-6.8-flash-lite"] = "reasoning" },
            },
            ApiKey = "key",
        };
        var vm = new RemoteModelEditViewData(source, _ => false);

        RemoteModelInfo result = vm.BuildResult();

        Assert.Equal("reasoning", result.Config.ObservedThinkingFields["sensenova-6.8-flash-lite"]);
    }
}
