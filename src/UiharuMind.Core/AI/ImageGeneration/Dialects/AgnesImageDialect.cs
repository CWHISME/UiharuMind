using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.ImageGeneration.Dialects;

/// <summary>
/// Agnes：生成与编辑同走 <c>/images/generations</c>，输入图放顶层 <c>image[]</c>。
/// 尺寸写档位（<c>2K</c>），比例另放 <c>ratio</c>，服务端缺省 1:1——所以编辑时比例
/// 必须由我们按主图推出来，否则一张竖图改完变方图。
/// 不传 <c>response_format</c>：文档要求它放 <c>extra_body</c>，那是 Python SDK 的写法，
/// 直发 HTTP 该怎么放没有实测；缺省返回 URL，由回退链统一下载。
/// </summary>
internal sealed class AgnesImageDialect : ImageDialectBase
{
    public AgnesImageDialect(HttpClient http) : base(http)
    {
    }

    internal override HttpRequestMessage BuildRequest(ImageModelInfo model, ImageRequest request, JsonObject extra)
    {
        JsonObject body = new()
        {
            ["model"] = model.ModelId,
            ["prompt"] = request.Prompt,
            ["size"] = model.Resolution.ToTierLabel(),
        };

        // 主图宽高读不出来时宁可不传，交服务端缺省，也不瞎猜
        if ((EffectiveRatio(request) ?? RatioOfPrimary(request)) is { } ratio) body["ratio"] = ratio.ToString();

        if (request.IsEdit)
        {
            JsonArray images = new();
            foreach (ImageInput image in request.Images) images.Add(ImageFormats.ToDataUrl(image.Bytes, image.MediaType));
            body["image"] = images;
        }

        return JsonRequest(ResolveUri(model.Endpoint, "/images/generations"), body, extra);
    }

    private static ImageAspectRatio? RatioOfPrimary(ImageRequest request)
    {
        return request.IsEdit && ImageFormats.TryReadSize(request.Images[0].Bytes, out int width, out int height)
            ? ImageAspectRatio.Nearest(width, height)
            : null;
    }
}
