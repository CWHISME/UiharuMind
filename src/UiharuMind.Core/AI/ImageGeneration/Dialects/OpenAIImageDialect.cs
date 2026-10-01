using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.ImageGeneration.Dialects;

/// <summary>
/// OpenAI 官方 <c>/images/*</c>（gpt-image 系）：生成是 JSON，编辑是 multipart、多图走 <c>image[]</c>。
/// 尺寸只有三档（方 / 横 / 竖），分辨率档不生效。不传 <c>response_format</c>：gpt-image 恒回 base64
/// 且会拒收这个参数，dall-e 缺省回 URL，解析两种都认。
///
/// 不用 M.E.AI 的 <c>AsIImageGenerator</c>：它编辑时只取第一张输入图，参考图被静默丢掉。
/// </summary>
internal sealed class OpenAIImageDialect : ImageDialectBase
{
    public OpenAIImageDialect(HttpClient http) : base(http)
    {
    }

    internal override HttpRequestMessage BuildRequest(ImageModelInfo model, ImageRequest request, JsonObject extra)
    {
        string size = SizeOf(EffectiveRatio(request));
        if (!request.IsEdit)
        {
            JsonObject body = new()
            {
                ["model"] = model.ModelId,
                ["prompt"] = request.Prompt,
                ["n"] = 1,
                ["size"] = size,
            };
            return JsonRequest(ResolveUri(model.Endpoint, "/images/generations"), body, extra);
        }

        MultipartFormDataContent form = new()
        {
            { new StringContent(model.ModelId), "model" },
            { new StringContent(request.Prompt), "prompt" },
            { new StringContent("1"), "n" },
            { new StringContent(size), "size" },
        };
        for (int i = 0; i < request.Images.Count; i++)
        {
            ByteArrayContent file = new(request.Images[i].Bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(request.Images[i].MediaType);
            form.Add(file, "image[]", $"image{i}{ImageFormats.ExtensionOf(request.Images[i].MediaType)}");
        }

        foreach ((string key, JsonNode? value) in extra) form.Add(new StringContent(FormValue(value)), key);

        return new HttpRequestMessage(HttpMethod.Post, ResolveUri(model.Endpoint, "/images/edits")) { Content = form };
    }

    private static string SizeOf(ImageAspectRatio? ratio)
    {
        if (ratio is not { } value) return "auto";
        if (value.IsLandscape) return "1536x1024";
        return value.IsPortrait ? "1024x1536" : "1024x1024";
    }

    // 表单字段只有字符串：字符串原样，其余（布尔、数字）取 JSON 字面
    private static string FormValue(JsonNode? value)
    {
        return value is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String
            ? scalar.GetValue<string>()
            : value?.ToJsonString() ?? string.Empty;
    }
}
