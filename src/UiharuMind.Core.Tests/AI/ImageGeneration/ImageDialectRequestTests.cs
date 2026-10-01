using System.Net;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.ImageGeneration.Dialects;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.AI.ImageGeneration;

/// <summary>
/// 三种接口格式各自的请求形状。各家文档的差异全在这一层，回退链看不到这些
/// </summary>
public class ImageDialectRequestTests
{
    private static readonly HttpClient Unused = new();

    private static ImageModelInfo Model(EImageDialect dialect) => new()
    {
        Name = "m", Dialect = dialect, Endpoint = "https://example.com/v1", ModelId = "model-x",
    };

    private static ImageRequest Generate(ImageAspectRatio? ratio = null) => new("a cat", [], ratio);

    private static ImageRequest Edit(params byte[][] images) =>
        new("make it blue", images.Select(i => new ImageInput(i, "image/png")).ToList(), null);

    private static async Task<JsonObject> JsonBodyOf(HttpRequestMessage message) =>
        (JsonObject)JsonNode.Parse(await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task SenseNova_Generation_WritesPixelSizeAndAsksForBase64()
    {
        HttpRequestMessage message = new SenseNovaImageDialect(Unused)
            .BuildRequest(Model(EImageDialect.SenseNova), Generate(), new JsonObject());
        JsonObject body = await JsonBodyOf(message);

        Assert.Equal("https://example.com/v1/images/generations", message.RequestUri!.ToString());
        Assert.Equal("2048x2048", body["size"]!.GetValue<string>());
        Assert.Equal("b64_json", body["response_format"]!.GetValue<string>());
        Assert.Equal(1, body["n"]!.GetValue<int>());
    }

    [Fact]
    public async Task SenseNova_Edit_UsesEditsPathAndFollowsPrimaryImage()
    {
        HttpRequestMessage message = new SenseNovaImageDialect(Unused)
            .BuildRequest(Model(EImageDialect.SenseNova), Edit(TestImages.Png(10, 20), TestImages.Png(5, 5)), new JsonObject());
        JsonObject body = await JsonBodyOf(message);

        Assert.Equal("https://example.com/v1/images/edits", message.RequestUri!.ToString());
        Assert.Equal("auto", body["size"]!.GetValue<string>());
        JsonArray images = body["images"]!.AsArray();
        Assert.Equal(2, images.Count);
        Assert.StartsWith("data:image/png;base64,", images[0]!["image_url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Agnes_Generation_WritesTierAndRatio()
    {
        ImageModelInfo model = Model(EImageDialect.Agnes);
        model.Resolution = EImageResolution.Res4K;

        JsonObject body = await JsonBodyOf(new AgnesImageDialect(Unused)
            .BuildRequest(model, Generate(new ImageAspectRatio(9, 16)), new JsonObject()));

        Assert.Equal("4K", body["size"]!.GetValue<string>());
        Assert.Equal("9:16", body["ratio"]!.GetValue<string>());
        Assert.False(body.ContainsKey("response_format"));
    }

    [Fact]
    public async Task Agnes_Edit_InfersRatioFromPrimaryImage()
    {
        // 服务端缺省 1:1：不推比例，一张横图改完就成了方图
        HttpRequestMessage message = new AgnesImageDialect(Unused)
            .BuildRequest(Model(EImageDialect.Agnes), Edit(TestImages.Png(1920, 1080)), new JsonObject());
        JsonObject body = await JsonBodyOf(message);

        Assert.Equal("https://example.com/v1/images/generations", message.RequestUri!.ToString());
        Assert.Equal("16:9", body["ratio"]!.GetValue<string>());
        Assert.Single(body["image"]!.AsArray());
    }

    [Fact]
    public async Task Agnes_Edit_OmitsRatioWhenPrimarySizeUnknown()
    {
        JsonObject body = await JsonBodyOf(new AgnesImageDialect(Unused)
            .BuildRequest(Model(EImageDialect.Agnes), Edit([1, 2, 3]), new JsonObject()));

        Assert.False(body.ContainsKey("ratio"));
    }

    [Fact]
    public async Task OpenAI_Generation_MapsRatioToFixedSizes()
    {
        OpenAIImageDialect dialect = new(Unused);
        ImageModelInfo model = Model(EImageDialect.OpenAI);

        Assert.Equal("1536x1024", (await JsonBodyOf(dialect.BuildRequest(model, Generate(new(16, 9)), new())))["size"]!.GetValue<string>());
        Assert.Equal("1024x1536", (await JsonBodyOf(dialect.BuildRequest(model, Generate(new(2, 3)), new())))["size"]!.GetValue<string>());
        Assert.Equal("1024x1024", (await JsonBodyOf(dialect.BuildRequest(model, Generate(), new())))["size"]!.GetValue<string>());
    }

    [Fact]
    public async Task OpenAI_Edit_SendsEveryImageAsMultipart()
    {
        // M.E.AI 的 AsIImageGenerator 只取第一张：这里钉住参考图不被丢
        HttpRequestMessage message = new OpenAIImageDialect(Unused).BuildRequest(Model(EImageDialect.OpenAI),
            Edit(TestImages.Png(1, 1), TestImages.Png(2, 2), TestImages.Png(3, 3)), new JsonObject { ["quality"] = "high" });

        MultipartFormDataContent form = Assert.IsType<MultipartFormDataContent>(message.Content);
        Assert.Equal("https://example.com/v1/images/edits", message.RequestUri!.ToString());
        Assert.Equal(3, form.Count(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "image[]"));
        HttpContent quality = form.Single(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "quality");
        Assert.Equal("high", await quality.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExtraBody_OverridesOurValues()
    {
        JsonObject body = await JsonBodyOf(new SenseNovaImageDialect(Unused).BuildRequest(Model(EImageDialect.SenseNova),
            Generate(), new JsonObject { ["watermark"] = false, ["size"] = "1024x1024" }));

        Assert.False(body["watermark"]!.GetValue<bool>());
        Assert.Equal("1024x1024", body["size"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("https://a.com/v1", "https://a.com/v1/images/edits")]
    [InlineData("https://a.com/v1/", "https://a.com/v1/images/edits")]
    [InlineData("https://a.com/v1/images/generations", "https://a.com/v1/images/edits")]
    public void ResolveUri_AcceptsRootOrFullEndpoint(string endpoint, string expected)
    {
        Assert.Equal(expected, ImageDialectBase.ResolveUri(endpoint, "/images/edits").ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, EImageFailureKind.Rejected)]
    [InlineData(HttpStatusCode.UnprocessableEntity, EImageFailureKind.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, EImageFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, EImageFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, EImageFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, EImageFailureKind.Unavailable)]
    public void Classify_OnlyServiceSideFailuresFallBack(HttpStatusCode status, EImageFailureKind expected)
    {
        Assert.Equal(expected, ImageDialectBase.Classify(status));
    }

    [Fact]
    public void ParseResponse_ReadsBase64UrlAndRevisedPrompt()
    {
        ImageOutcome outcome = ImageDialectBase.ParseResponse(
            """{"data":[{"b64_json":"AQI=","revised_prompt":"a fluffy cat"},{"url":"https://cdn/x.png","b64_json":null}]}""");

        Assert.Null(outcome.Failure);
        Assert.Equal([1, 2], outcome.Images[0].Bytes);
        Assert.Equal("https://cdn/x.png", outcome.Images[1].Url);
        Assert.Equal("a fluffy cat", outcome.RevisedPrompt);
    }

    [Theory]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"created":1}""")]
    [InlineData("not json")]
    public void ParseResponse_SuccessWithoutImage_MayBeCharged(string json)
    {
        Assert.Equal(EImageFailureKind.MaybeCharged, ImageDialectBase.ParseResponse(json).Failure?.Kind);
    }
}
