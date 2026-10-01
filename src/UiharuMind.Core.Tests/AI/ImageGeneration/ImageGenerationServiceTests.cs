using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.ImageGeneration.Dialects;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.AI.ImageGeneration;

/// <summary>
/// 生图回退（ADR 0052）：只有「这一家现在用不了」才换下一个，其余一律停下
/// </summary>
public class ImageGenerationServiceTests
{
    /// <summary>按模型名回放预设结果的假适配器，并记下问过谁</summary>
    private sealed class ScriptedDialect(Dictionary<string, ImageOutcome> outcomes) : IImageDialect
    {
        public List<string> Asked { get; } = new();

        public Task<ImageOutcome> GenerateAsync(ImageModelInfo model, ImageRequest request, CancellationToken ct)
        {
            Asked.Add(model.Name);
            return Task.FromResult(outcomes[model.Name]);
        }
    }

    private static readonly byte[] Png = TestImages.Png(4, 4);

    // 熔断记账是进程级静态的，每个用例各用一套名字
    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}";

    private static ImageModelInfo Model(string name, bool supportsEditing = true) => new()
    {
        Name = name, Endpoint = "https://example.com/v1", ModelId = "x", SupportsEditing = supportsEditing,
    };

    private static ImageOutcome Drew(byte[]? bytes = null, string? url = null) =>
        new() { Images = [new ImageOutput(bytes, url)] };

    private static (ImageGenerationService Service, ScriptedDialect Dialect) Build(
        IReadOnlyList<ImageModelInfo> models, Dictionary<string, ImageOutcome> outcomes,
        Func<string, CancellationToken, Task<byte[]>>? download = null)
    {
        ScriptedDialect dialect = new(outcomes);
        return (new ImageGenerationService(() => models, _ => dialect,
            download ?? ((_, _) => throw new InvalidOperationException("unexpected download"))), dialect);
    }

    private static Task<ImageGenerationReport> Generate(ImageGenerationService service, bool edit = false) =>
        service.GenerateAsync(new ImageRequest("cat", edit ? [new ImageInput(Png, "image/png")] : [], null),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Unavailable_FallsBackAndSaysWhy()
    {
        string first = Unique("first");
        string second = Unique("second");
        var (service, _) = Build([Model(first), Model(second)], new()
        {
            [first] = ImageOutcome.Fail(EImageFailureKind.Unavailable, "HTTP 503"),
            [second] = Drew(Png),
        });

        ImageGenerationReport report = await Generate(service);

        Assert.Null(report.Failure);
        Assert.Equal(second, report.ModelName);
        Assert.Equal("image/png", report.Images.Single().MediaType);
        SkippedImageModel skipped = Assert.Single(report.Skipped);
        Assert.Equal((first, "HTTP 503"), (skipped.Name, skipped.Reason));
    }

    [Theory]
    [InlineData(EImageFailureKind.Rejected)]
    [InlineData(EImageFailureKind.MaybeCharged)]
    public async Task OtherFailures_StopWithoutTryingNext(EImageFailureKind kind)
    {
        string first = Unique("first");
        string second = Unique("second");
        var (service, dialect) = Build([Model(first), Model(second)], new()
        {
            [first] = ImageOutcome.Fail(kind, "nope"),
            [second] = Drew(Png),
        });

        ImageGenerationReport report = await Generate(service);

        Assert.Equal(kind, report.Failure!.Kind);
        Assert.Equal(first, report.ModelName);
        Assert.Equal([first], dialect.Asked);
    }

    [Fact]
    public async Task Edit_SkipsModelsWithoutEditingSilently()
    {
        string drawOnly = Unique("draw-only");
        string editor = Unique("editor");
        var (service, dialect) = Build([Model(drawOnly, supportsEditing: false), Model(editor)], new()
        {
            [editor] = Drew(Png),
        });

        ImageGenerationReport report = await Generate(service, edit: true);

        Assert.Equal(editor, report.ModelName);
        Assert.Empty(report.Skipped); //不支持编辑不算回退
        Assert.Equal([editor], dialect.Asked);
    }

    [Fact]
    public async Task NothingEligible_ExplainsEveryModel()
    {
        string drawOnly = Unique("draw-only");
        var (service, _) = Build([Model(drawOnly, supportsEditing: false), new ImageModelInfo { Name = "blank" }], new());

        ImageGenerationReport report = await Generate(service, edit: true);

        Assert.Equal(EImageFailureKind.Unavailable, report.Failure!.Kind);
        Assert.Contains("does not support editing", report.Failure.Message);
        Assert.Contains("blank (not configured)", report.Failure.Message);
    }

    [Fact]
    public async Task UrlOutput_IsDownloaded()
    {
        string name = Unique("url");
        var (service, _) = Build([Model(name)], new() { [name] = Drew(url: "https://cdn/x.png") },
            (_, _) => Task.FromResult(Png));

        ImageGenerationReport report = await Generate(service);

        Assert.Equal(Png, report.Images.Single().Bytes);
    }

    [Fact]
    public async Task DownloadFailure_MayBeChargedAndKeepsTheLink()
    {
        string name = Unique("url");
        var (service, _) = Build([Model(name)], new() { [name] = Drew(url: "https://cdn/x.png") },
            (_, _) => throw new HttpRequestException("gone"));

        ImageGenerationReport report = await Generate(service);

        Assert.Equal(EImageFailureKind.MaybeCharged, report.Failure!.Kind);
        Assert.Contains("https://cdn/x.png", report.Failure.Message);
    }

    [Fact]
    public async Task NonImageBytes_AreNotPassedOffAsImages()
    {
        string name = Unique("html");
        var (service, _) = Build([Model(name)], new() { [name] = Drew("<html>"u8.ToArray()) });

        ImageGenerationReport report = await Generate(service);

        Assert.Equal(EImageFailureKind.MaybeCharged, report.Failure!.Kind);
    }

    [Fact]
    public async Task RepeatedUnavailability_TripsTheCircuit()
    {
        string flaky = Unique("flaky");
        string backup = Unique("backup");
        var (service, dialect) = Build([Model(flaky), Model(backup)], new()
        {
            [flaky] = ImageOutcome.Fail(EImageFailureKind.Unavailable, "HTTP 500"),
            [backup] = Drew(Png),
        });

        for (int i = 0; i < 3; i++) await Generate(service);
        dialect.Asked.Clear();
        ImageGenerationReport report = await Generate(service);

        Assert.Equal([backup], dialect.Asked);
        Assert.Contains("repeated failures", Assert.Single(report.Skipped).Reason);
    }

    [Fact]
    public void HasConfiguredModel_IgnoresBlankEntries()
    {
        Assert.False(Build([new ImageModelInfo { Name = "blank" }], new()).Service.HasConfiguredModel);
        Assert.True(Build([Model("ok")], new()).Service.HasConfiguredModel);
    }
}
