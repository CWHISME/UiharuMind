using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;
using UiharuMind.Features.Models;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 编辑窗口的思考档位候选（ADR 0098）：跟随模型声明的取值，不再固定四档；
/// 声明集合外的旧值退回「不干预」，没声明的模型保留用户已选值。
/// </summary>
public class RemoteModelEditThinkingTests
{
    private sealed class EmptyCatalogFetcher : IModelCatalogFetcher
    {
        public Task<IReadOnlyList<ModelCatalogEntry>> FetchAsync(string catalogUrl, string? apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModelCatalogEntry>>([]);
    }

    private sealed class EmptyCatalogCache : IModelCatalogCache
    {
        public IReadOnlyList<ModelCatalogEntry>? TryRead(string catalogUrl) => [];

        public void Write(string catalogUrl, IReadOnlyList<ModelCatalogEntry> entries)
        {
        }
    }

    private static RemoteModelEditViewData CreateEditVm(BaseRemoteModelConfig config) =>
        new(
            new RemoteModelInfo { Config = config, ApiKey = "test-key" },
            _ => false,
            new EmptyCatalogFetcher(),
            new EmptyCatalogCache());

    private static string[] Values(RemoteModelEditViewData vm) =>
        vm.ThinkingOptions.Select(option => option.Value).ToArray();

    [Fact]
    public void UndeclaredModel_OffersFallbackLadder()
    {
        var vm = CreateEditVm(new RemoteModelConfig { ModelId = "some-model" });

        Assert.Equal(["", "none", "low", "medium", "high", "max"], Values(vm));
    }

    [Fact]
    public void DeclaredModel_OffersDeclaredValuesOnly()
    {
        var vm = CreateEditVm(new RemoteCommandCodeModelConfig { ModelId = "Qwen/Qwen3.8-27B" });

        string[] values = Values(vm);

        Assert.Contains("xhigh", values);
        Assert.DoesNotContain("max", values);
    }

    [Fact]
    public void StoredEffortOutsideDeclaration_FallsBackToAuto()
    {
        var vm = CreateEditVm(new RemoteCommandCodeModelConfig
        {
            ModelId = "Qwen/Qwen3.8-27B",
            ThinkingEffort = "max",
        });

        Assert.Equal(ThinkingEfforts.Auto, vm.SelectedThinkingOption!.Value);
    }

    [Fact]
    public void UndeclaredModel_KeepsStoredEffort()
    {
        var vm = CreateEditVm(new RemoteModelConfig { ModelId = "some-model", ThinkingEffort = "xhigh" });

        Assert.Equal("xhigh", vm.SelectedThinkingOption!.Value);
    }

    [Fact]
    public void DeclaredNone_DoesNotDuplicateTheOffOption()
    {
        var vm = CreateEditVm(new RemoteOpenCodeModelConfig { ModelId = "gpt-5.2" });

        string[] values = Values(vm);

        Assert.Single(values, "none");
        Assert.Equal(["", "none", "low", "medium", "high", "xhigh"], values);
    }

    [Theory]
    [InlineData("minimal")]
    [InlineData("xhigh")]
    public void KnownEffortValues_GetLocalizedLabel(string value) =>
        Assert.NotEqual(value, new ThinkingOptionItem(value).Display);

    [Fact]
    public void UnknownEffortValue_ShowsRaw() =>
        Assert.Equal("some-weird-value", new ThinkingOptionItem("some-weird-value").Display);

    [Fact]
    public void BuildResult_WritesSelectedEffort()
    {
        var vm = CreateEditVm(new RemoteModelConfig { ModelId = "some-model" });
        vm.SelectedThinkingOption = vm.ThinkingOptions.First(option => option.Value == "high");

        Assert.Equal("high", vm.BuildResult().Config.ThinkingEffort);
    }
}
