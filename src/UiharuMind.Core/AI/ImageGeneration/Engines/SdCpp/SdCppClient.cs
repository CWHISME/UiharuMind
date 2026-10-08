using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// sd-server 某一时刻的任务状态
/// </summary>
/// <param name="Status">queued / generating / completed / failed / cancelled</param>
/// <param name="Images">出好的图（completed 时）</param>
/// <param name="Step">当前步</param>
/// <param name="TotalSteps">这一轮共几步</param>
/// <param name="Preview">中间预览图</param>
/// <param name="ErrorCode">失败时的错误码</param>
/// <param name="ErrorMessage">失败时的原因</param>
public sealed record SdCppJob(
    string Status,
    IReadOnlyList<byte[]> Images,
    int Step,
    int TotalSteps,
    byte[]? Preview,
    string ErrorCode,
    string ErrorMessage);

/// <summary>
/// sd-server 原生接口（/sdcpp/v1）的异步任务：提交、轮询、撤销排队中的。OpenAI 兼容那组是同步的、没有采样参数，不用
/// </summary>
/// <param name="http">本机回环用的客户端（不设总超时，由调用方的取消控制）</param>
public sealed class SdCppClient(HttpClient http)
{
    /// <summary>
    /// 提交一次出图
    /// </summary>
    /// <param name="server">服务根地址</param>
    /// <param name="request">请求</param>
    /// <param name="vaeTiling">VAE 分块解码</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>任务 id</returns>
    /// <exception cref="SdCppRequestException">参数不合法</exception>
    public async Task<string> SubmitAsync(Uri server, SdCppImageRequest request, bool vaeTiling,
        CancellationToken cancellationToken)
    {
        // 带长度的正文：不走分块传输，sd-server 的 HTTP 库对分块请求体支持不一
        using StringContent body = new(ToJson(request, vaeTiling).ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync(new Uri(server, "sdcpp/v1/img_gen"), body,
            cancellationToken).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new SdCppRequestException(ErrorMessage(text));
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(text)?["id"]?.GetValue<string>()
               ?? throw new HttpRequestException("sd-server accepted the job without an id.");
    }

    /// <summary>
    /// 查任务状态
    /// </summary>
    /// <param name="server">服务根地址</param>
    /// <param name="id">任务 id</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>状态</returns>
    public async Task<SdCppJob> GetJobAsync(Uri server, string id, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri(server, $"sdcpp/v1/jobs/{id}"),
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ParseJob(doc.RootElement);
    }

    /// <summary>
    /// 撤销任务（只有还在排队的撤得掉）；撤不掉不抛
    /// </summary>
    /// <param name="server">服务根地址</param>
    /// <param name="id">任务 id</param>
    public async Task TryCancelAsync(Uri server, string id)
    {
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            using HttpResponseMessage response = await http.PostAsync(new Uri(server, $"sdcpp/v1/jobs/{id}/cancel"),
                null, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            Log.Debug($"Cancel sd-server job {id} failed: {e.Message}");
        }
    }

    /// <summary>
    /// 请求体（字段见 sd.cpp 的 examples/server/api.md）；没给的采样参数不传，交给 sd.cpp 的默认
    /// </summary>
    public static JsonObject ToJson(SdCppImageRequest request, bool vaeTiling)
    {
        SdCppSampling sampling = request.Sampling;
        JsonObject guidance = new() { ["txt_cfg"] = sampling.CfgScale };
        if (sampling.DistilledGuidance is { } distilled) guidance["distilled_guidance"] = distilled;
        JsonObject sample = new() { ["sample_steps"] = sampling.Steps, ["guidance"] = guidance };
        if (!string.IsNullOrEmpty(sampling.Sampler)) sample["sample_method"] = sampling.Sampler;
        if (!string.IsNullOrEmpty(sampling.Scheduler)) sample["scheduler"] = sampling.Scheduler;
        if (sampling.FlowShift is { } shift) sample["flow_shift"] = shift;

        JsonArray references = [];
        foreach (byte[] image in request.ReferenceImages) references.Add(Convert.ToBase64String(image));
        return new JsonObject
        {
            ["prompt"] = request.Prompt,
            ["negative_prompt"] = request.NegativePrompt,
            ["width"] = request.Width,
            ["height"] = request.Height,
            ["seed"] = request.Seed,
            ["batch_count"] = 1,
            ["ref_images"] = references,
            ["sample_params"] = sample,
            ["vae_tiling_params"] = new JsonObject { ["enabled"] = vaeTiling },
            // 潜空间直接投影成预览，几乎不花时间
            ["preview"] = "proj",
            ["output_format"] = "png"
        };
    }

    private static SdCppJob ParseJob(JsonElement root)
    {
        string status = root.TryGetProperty("status", out JsonElement s) ? s.GetString() ?? "" : "";
        List<byte[]> images = [];
        if (root.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("images", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            foreach (JsonElement image in list.EnumerateArray())
                if (image.TryGetProperty("b64_json", out JsonElement b64) && b64.GetString() is { Length: > 0 } data)
                    images.Add(Convert.FromBase64String(data));

        int step = 0;
        int total = 0;
        byte[]? preview = null;
        if (root.TryGetProperty("preview", out JsonElement p) && p.ValueKind == JsonValueKind.Object)
        {
            if (p.TryGetProperty("step", out JsonElement st) && st.ValueKind == JsonValueKind.Number) step = st.GetInt32();
            if (p.TryGetProperty("total_steps", out JsonElement tt) && tt.ValueKind == JsonValueKind.Number)
                total = tt.GetInt32();
            if (p.TryGetProperty("b64_json", out JsonElement pb) && pb.GetString() is { Length: > 0 } frame)
                preview = Convert.FromBase64String(frame);
        }

        string code = "";
        string message = "";
        if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)
        {
            if (error.TryGetProperty("code", out JsonElement c)) code = c.GetString() ?? "";
            if (error.TryGetProperty("message", out JsonElement m)) message = m.GetString() ?? "";
        }

        return new SdCppJob(status, images, step, total, preview, code, message);
    }

    // 400 的正文有的是 {"error":{"message":…}}，有的是 {"error":"…"}，都不是就原样截一段
    private static string ErrorMessage(string text)
    {
        try
        {
            JsonNode? error = JsonNode.Parse(text)?["error"];
            if (error is JsonObject obj && obj["message"]?.GetValue<string>() is { Length: > 0 } message) return message;
            if (error is JsonValue value && value.TryGetValue(out string? plain) && plain.Length > 0) return plain;
        }
        catch (JsonException)
        {
        }

        return text.Length > 300 ? text[..300] : text;
    }
}
