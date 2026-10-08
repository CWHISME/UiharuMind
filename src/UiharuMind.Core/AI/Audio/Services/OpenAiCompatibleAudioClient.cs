using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UiharuMind.Core.AI.Audio;

/// <summary>OpenAI 兼容音频端点的连接信息</summary>
/// <param name="BaseUrl">服务根地址：http://127.0.0.1:18082 或 https://api.openai.com/v1 都行，缺 /v1 会自动补</param>
/// <param name="ApiKey">Bearer 密钥；本地引擎（audio.cpp）不需要</param>
public sealed record OpenAiAudioEndpoint(string BaseUrl, string? ApiKey = null);

/// <summary>
/// 一个 OpenAI 兼容音频客户端，本地 audio.cpp 与远程服务商共用（ADR 0071 后果）。
/// 非流式合成 / 听写 / 流式合成三种走法；参数差异由调用方按 provider 声明显式指定，不猜。
/// </summary>
public sealed class OpenAiCompatibleAudioClient : IDisposable
{
    private readonly OpenAiAudioEndpoint _endpoint;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public OpenAiCompatibleAudioClient(OpenAiAudioEndpoint endpoint, HttpClient? http = null)
    {
        _endpoint = endpoint;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _ownsHttp = http is null;
    }

    /// <summary>
    /// 服务根地址（构造时给的原样）
    /// </summary>
    public string BaseUrl => _endpoint.BaseUrl;

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    /// <summary>非流式合成：POST /v1/audio/speech，按 <paramref name="format"/> 显式指定 response_format</summary>
    /// <param name="seed">随机种子（audio.cpp 的扩展字段）；null 不传</param>
    /// <param name="speed">语速倍率；null 或 1 不传</param>
    /// <param name="instructions">语气描述（OpenAI 的 instructions 字段）；空不传</param>
    /// <param name="options">模型专有参数（audio.cpp 的 options 扩展字段）；空不传</param>
    public async Task<AudioSynthesisResult> SynthesizeAsync(
        string model, string text, string? voice, EAudioFormat format, CancellationToken cancellationToken,
        int? seed = null, float? speed = null, string? instructions = null,
        IReadOnlyDictionary<string, string>? options = null)
    {
        using JsonDocument body = JsonDocument.Parse(
            BuildSpeechJson(model, text, voice, format, stream: false, seed, speed, instructions, options));
        using HttpRequestMessage request = new(HttpMethod.Post, SpeechUrl());
        request.Content = new StringContent(body.RootElement.GetRawText(), Encoding.UTF8, "application/json");
        ApplyAuth(request);

        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        // 非流式：整段读完再返回（response 会被释放，流不能带出去）；流式走 SynthesizeStreamingAsync
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new AudioSynthesisResult(new MemoryStream(bytes), format);
    }

    /// <summary>听写：POST /v1/audio/transcriptions（multipart），audio 流只读不关</summary>
    public async Task<AudioTranscriptionResult> TranscribeAsync(
        string model, Stream audio, EAudioFormat format, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, TranscriptionUrl());
        using MultipartFormDataContent form = new();
        form.Add(new StringContent(model), "model");
        form.Add(new ByteArrayContent(await ReadAllAsync(audio, cancellationToken).ConfigureAwait(false)),
            "file", "audio" + FormatExtension(format));
        request.Content = form;
        ApplyAuth(request);

        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        string text = doc.RootElement.TryGetProperty("text", out JsonElement textElement)
            ? textElement.GetString() ?? ""
            : "";
        return new AudioTranscriptionResult(text);
    }

    /// <summary>
    /// 列音色：GET /v1/audio/voices?model=（audio.cpp 的扩展端点，OpenAI 官方没有）
    /// </summary>
    public async Task<IReadOnlyList<string>> GetVoicesAsync(string model, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get,
            $"{NormalizeBase()}/audio/voices?model={Uri.EscapeDataString(model)}");
        ApplyAuth(request);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("voices", out JsonElement voices) || voices.ValueKind != JsonValueKind.Array)
            return [];
        return voices.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .ToList();
    }

    /// <summary>
    /// 流式合成。Sse：audio.cpp 形状，SSE 里 base64 PCM 块（response_format=pcm，需自备采样率）；
    /// Raw：OpenAI 形状，响应体就是音频字节块（mp3/wav）。返回的流边收边读。
    /// </summary>
    public async Task<AudioSynthesisResult> SynthesizeStreamingAsync(
        string model, string text, string? voice, EAudioFormat format,
        EAudioStreamMode mode, int? sampleRateHz, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, SpeechUrl());
        request.Content = new StringContent(BuildSpeechJson(model, text, voice, format, stream: true), Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mode == EAudioStreamMode.Sse ? "text/event-stream" : "audio/*"));
        ApplyAuth(request);

        HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        // 调用方释放流（打断）或取消请求，都要停掉后台搬运并释放响应
        CancellationTokenSource pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ProducerConsumerStream output = new(() =>
        {
            try
            {
                pumpCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已经收完
            }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                Stream input = await response.Content.ReadAsStreamAsync(pumpCts.Token).ConfigureAwait(false);
                if (mode == EAudioStreamMode.Sse)
                    await PumpSseAsync(input, output, pumpCts.Token).ConfigureAwait(false);
                else
                    await PumpRawAsync(input, output, pumpCts.Token).ConfigureAwait(false);
                output.Complete();
            }
            catch (Exception e)
            {
                output.Fail(e);
            }
            finally
            {
                response.Dispose();
                pumpCts.Dispose();
            }
        }, CancellationToken.None);

        return new AudioSynthesisResult(output, format, sampleRateHz);
    }

    private static async Task PumpRawAsync(Stream input, ProducerConsumerStream output, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SSE：data: {"type":"speech.audio.delta","audio":"<base64>"} … data: [DONE]</summary>
    private static async Task PumpSseAsync(Stream input, ProducerConsumerStream output, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(input, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string payload = line["data:".Length..].Trim();
            if (payload == "[DONE]") return;

            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;
            string type = root.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() ?? "" : "";
            if (type == "speech.audio.delta")
            {
                if (root.TryGetProperty("audio", out JsonElement audioElement))
                {
                    byte[] chunk = Convert.FromBase64String(audioElement.GetString() ?? "");
                    await output.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (type == "error")
            {
                string message = root.TryGetProperty("message", out JsonElement messageElement) ? messageElement.GetString() ?? "" : "";
                throw new InvalidOperationException($"Audio stream error: {message}");
            }
            // speech.audio.done 之后的 [DONE] 收尾，不额外处理
        }
    }

    private string BuildSpeechJson(string model, string text, string? voice, EAudioFormat format, bool stream,
        int? seed = null, float? speed = null, string? instructions = null,
        IReadOnlyDictionary<string, string>? options = null)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteString("input", text);
            if (!string.IsNullOrWhiteSpace(voice)) writer.WriteString("voice", voice);
            writer.WriteString("response_format", FormatName(format, stream));
            if (stream) writer.WriteBoolean("stream", true);
            if (seed is { } value) writer.WriteNumber("seed", value);
            if (speed is { } rate and not 1f) writer.WriteNumber("speed", Math.Round(rate, 2));
            if (!string.IsNullOrWhiteSpace(instructions)) writer.WriteString("instructions", instructions);
            if (options is { Count: > 0 })
            {
                writer.WriteStartObject("options");
                foreach ((string key, string option) in options) writer.WriteString(key, option);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string FormatName(EAudioFormat format, bool stream)
    {
        if (stream && format == EAudioFormat.Pcm16) return "pcm";
        return format switch
        {
            EAudioFormat.Wav => "wav",
            EAudioFormat.Mp3 => "mp3",
            EAudioFormat.Aiff => "aiff",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static string FormatExtension(EAudioFormat format) => format switch
    {
        EAudioFormat.Wav => ".wav",
        EAudioFormat.Mp3 => ".mp3",
        EAudioFormat.Aiff => ".aiff",
        EAudioFormat.Pcm16 => ".pcm",
        _ => ".bin"
    };

    private Uri SpeechUrl() => new($"{NormalizeBase()}/audio/speech");
    private Uri TranscriptionUrl() => new($"{NormalizeBase()}/audio/transcriptions");

    private string NormalizeBase()
    {
        string baseUrl = _endpoint.BaseUrl.TrimEnd('/');
        return baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? baseUrl : baseUrl + "/v1";
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_endpoint.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _endpoint.ApiKey);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw AudioServiceException.FromResponse(response.StatusCode, body);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}

/// <summary>流式合成的传输形状：Sse = audio.cpp 的 base64 PCM 事件流；Raw = OpenAI 的音频字节流</summary>
public enum EAudioStreamMode
{
    Sse,
    Raw
}
