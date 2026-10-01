using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.ImageGeneration.Dialects;

/// <summary>
/// 商汤 SenseNova：生成走 <c>/images/generations</c>，编辑走 <c>/images/edits</c>——两者都是 JSON，
/// 编辑图放 <c>images[].image_url</c>（第 1 张为主图）。尺寸写 <c>宽x高</c>，须是 32 的倍数、单边 512~4096；
/// 编辑缺省比例时写 <c>auto</c> 跟主图。<c>n</c> 只支持 1。
/// 一律要 <c>b64_json</c>：URL 只活 24 小时，省一次下载也省一次失效。
/// </summary>
internal sealed class SenseNovaImageDialect : ImageDialectBase
{
    private const int SizeMultiple = 32;
    private const int MinEdge = 512;
    private const int MaxEdge = 4096;

    public SenseNovaImageDialect(HttpClient http) : base(http)
    {
    }

    internal override HttpRequestMessage BuildRequest(ImageModelInfo model, ImageRequest request, JsonObject extra)
    {
        JsonObject body = new()
        {
            ["model"] = model.ModelId,
            ["prompt"] = request.Prompt,
            ["n"] = 1,
            ["size"] = SizeOf(model, request),
            ["response_format"] = "b64_json",
        };

        if (!request.IsEdit) return JsonRequest(ResolveUri(model.Endpoint, "/images/generations"), body, extra);

        JsonArray images = new();
        foreach (ImageInput image in request.Images)
        {
            images.Add(new JsonObject { ["image_url"] = ImageFormats.ToDataUrl(image.Bytes, image.MediaType) });
        }

        body["images"] = images;
        return JsonRequest(ResolveUri(model.Endpoint, "/images/edits"), body, extra);
    }

    private static string SizeOf(ImageModelInfo model, ImageRequest request)
    {
        if (EffectiveRatio(request) is not { } ratio) return "auto";

        (int width, int height) = ImageSizing.ToPixels(ratio, model.Resolution, SizeMultiple, MinEdge, MaxEdge);
        return $"{width}x{height}";
    }
}
