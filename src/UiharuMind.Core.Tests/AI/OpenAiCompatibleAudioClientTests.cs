using System.Net;
using System.Net.Sockets;
using System.Text;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// OpenAI 兼容音频客户端：本地 audio.cpp 与远程服务商共用同一形状（ADR 0071）。
/// 用本机假 server 验证请求形状、错误处理、SSE 流式拼接与渐进可读。
/// </summary>
public class OpenAiCompatibleAudioClientTests : IDisposable
{
    private readonly FakeAudioServer _server = new();
    private readonly OpenAiCompatibleAudioClient _client;
    private readonly OpenAiAudioEndpoint _endpoint;

    public OpenAiCompatibleAudioClientTests()
    {
        _endpoint = new OpenAiAudioEndpoint(_server.BaseUrl);
        _client = new OpenAiCompatibleAudioClient(_endpoint);
    }

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task Synthesize_PostsJson_WithExplicitFormat()
    {
        byte[] payload = [0x52, 0x49, 0x46, 0x46, 1, 2, 3];
        _server.SpeechMode = FakeAudioServer.EMode.Bytes;
        _server.BytesPayload = payload;

        AudioSynthesisResult synthesis = await _client.SynthesizeAsync("pocket-tts", "hello", "alba", EAudioFormat.Wav, CancellationToken.None);

        using MemoryStream read = new();
        await synthesis.Audio.CopyToAsync(read, TestContext.Current.CancellationToken);
        Assert.Equal(payload, read.ToArray());
        Assert.Equal(EAudioFormat.Wav, synthesis.Format);
        Assert.Equal("/v1/audio/speech", _server.LastPath);

        string json = _server.LastSpeechJson!;
        Assert.Contains("\"model\":\"pocket-tts\"", json);
        Assert.Contains("\"input\":\"hello\"", json);
        Assert.Contains("\"voice\":\"alba\"", json);
        Assert.Contains("\"response_format\":\"wav\"", json);
        Assert.DoesNotContain("\"stream\"", json);
    }

    [Theory]
    [InlineData("http://127.0.0.1:{port}")]
    [InlineData("http://127.0.0.1:{port}/v1")]
    public async Task BaseUrl_WithOrWithoutV1_ResolvesSamePath(string template)
    {
        var client = new OpenAiCompatibleAudioClient(new OpenAiAudioEndpoint(template.Replace("{port}", _server.Port.ToString())));
        _server.SpeechMode = FakeAudioServer.EMode.Bytes;

        await client.SynthesizeAsync("m", "t", null, EAudioFormat.Wav, CancellationToken.None);

        Assert.Equal("/v1/audio/speech", _server.LastPath);
    }

    [Fact]
    public async Task Synthesize_HttpError_ThrowsWithBody()
    {
        _server.SpeechMode = FakeAudioServer.EMode.Error;
        _server.ErrorStatus = 400;
        _server.ErrorBody = "{\"error\":\"bad voice\"}";

        AudioServiceException ex = await Assert.ThrowsAsync<AudioServiceException>(() =>
            _client.SynthesizeAsync("m", "t", null, EAudioFormat.Wav, CancellationToken.None));

        Assert.Contains("bad voice", ex.Message);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.StatusCode); //仍是 HttpRequestException，旧的 catch 照样接得住
    }

    [Fact]
    public async Task Transcribe_PostsMultipart_ReturnsText()
    {
        byte[] wav = [0x52, 0x49, 0x46, 0x46, 9, 9, 9];
        _server.TranscriptionResultText = "hello, this is a test";

        AudioTranscriptionResult result = await _client.TranscribeAsync(
            "moonshine-asr", new MemoryStream(wav), EAudioFormat.Wav, CancellationToken.None);

        Assert.Equal("hello, this is a test", result.Text);
        string body = Encoding.UTF8.GetString(_server.LastTranscriptionBody!);
        Assert.Contains("name=model", body);
        Assert.Contains("moonshine-asr", body);
        Assert.Contains("name=file", body);
        Assert.Contains("audio.wav", body);
        Assert.Contains(Encoding.UTF8.GetString(wav), body);
    }

    [Fact]
    public async Task Streaming_Sse_ConcatenatesBase64Deltas()
    {
        byte[] a = [1, 2, 3, 4];
        byte[] b = [5, 6, 7];
        byte[] c = [8, 9];
        _server.SpeechMode = FakeAudioServer.EMode.Sse;
        _server.SseDeltas = [Convert.ToBase64String(a), Convert.ToBase64String(b), Convert.ToBase64String(c)];

        AudioSynthesisResult result = await _client.SynthesizeStreamingAsync(
            "pocket-tts-stream", "hello", null, EAudioFormat.Pcm16, EAudioStreamMode.Sse, 24000, CancellationToken.None);

        Assert.Equal(EAudioFormat.Pcm16, result.Format);
        Assert.Equal(24000, result.SampleRateHz);

        using MemoryStream read = new();
        await result.Audio.CopyToAsync(read, TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], read.ToArray());
    }

    [Fact]
    public async Task Streaming_Sse_FirstChunkArrivesBeforeWholePayload()
    {
        _server.SpeechMode = FakeAudioServer.EMode.Sse;
        _server.SseDeltas = ["AA==", "AQ==", "Ag=="];
        _server.SseGapMs = 600; // 三段共约 1.2s 才发完

        AudioSynthesisResult result = await _client.SynthesizeStreamingAsync(
            "pocket-tts-stream", "hello", null, EAudioFormat.Pcm16, EAudioStreamMode.Sse, 24000, CancellationToken.None);

        DateTime start = DateTime.UtcNow;
        byte[] first = new byte[1];
        int read = await result.Audio.ReadAsync(first, TestContext.Current.CancellationToken);
        double firstChunkMs = (DateTime.UtcNow - start).TotalMilliseconds;

        // 第一块在第一个事件（~600ms）就到了；若客户端等整段，1.2s 后才会有一字节
        Assert.Equal(1, read);
        Assert.True(firstChunkMs < 1000, $"first chunk took {firstChunkMs:F0}ms");
    }

    [Fact]
    public async Task Streaming_Sse_ErrorEvent_FailsRead()
    {
        _server.SpeechMode = FakeAudioServer.EMode.Sse;
        _server.SseDeltas = ["AA=="];
        _server.SseErrorEvent = true;

        AudioSynthesisResult result = await _client.SynthesizeStreamingAsync(
            "pocket-tts-stream", "hello", null, EAudioFormat.Pcm16, EAudioStreamMode.Sse, 24000, CancellationToken.None);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using MemoryStream read = new();
            await result.Audio.CopyToAsync(read, TestContext.Current.CancellationToken);
        });
        Assert.Contains("Audio stream error", ex.Message);
    }

    [Fact]
    public async Task Streaming_Raw_CopiesAudioBytes()
    {
        byte[] mp3 = [0xFF, 0xFB, 1, 2, 3, 4, 5];
        _server.SpeechMode = FakeAudioServer.EMode.Raw;
        _server.BytesPayload = mp3;

        AudioSynthesisResult result = await _client.SynthesizeStreamingAsync(
            "gpt-4o-mini-tts", "hello", null, EAudioFormat.Mp3, EAudioStreamMode.Raw, null, CancellationToken.None);

        using MemoryStream read = new();
        await result.Audio.CopyToAsync(read, TestContext.Current.CancellationToken);
        Assert.Equal(mp3, read.ToArray());
        Assert.Equal(EAudioFormat.Mp3, result.Format);
    }

    [Fact]
    public async Task ApiKey_SentAsBearer()
    {
        var client = new OpenAiCompatibleAudioClient(new OpenAiAudioEndpoint(_server.BaseUrl, "sk-test-123"));
        _server.SpeechMode = FakeAudioServer.EMode.Bytes;

        await client.SynthesizeAsync("m", "t", null, EAudioFormat.Wav, CancellationToken.None);

        Assert.Equal("Bearer sk-test-123", _server.LastAuthorization);
    }

    /// <summary>本机假音频 server：按路由应答，记录最后一个请求，供断言</summary>
    private sealed class FakeAudioServer : IDisposable
    {
        public enum EMode { Bytes, Sse, Raw, Error }

        private readonly HttpListener _listener = new();

        public FakeAudioServer()
        {
            Port = FreePort();
            BaseUrl = $"http://127.0.0.1:{Port}";
            _listener.Prefixes.Add($"{BaseUrl}/");
            _listener.Start();
            _ = Task.Run(AcceptLoop);
        }

        public int Port { get; }
        public string BaseUrl { get; }
        public EMode SpeechMode { get; set; } = EMode.Bytes;
        public byte[] BytesPayload { get; set; } = [1, 2, 3];
        public string[] SseDeltas { get; set; } = [];
        public bool SseErrorEvent { get; set; }
        public int SseGapMs { get; set; }
        public int ErrorStatus { get; set; } = 500;
        public string ErrorBody { get; set; } = "boom";
        public string TranscriptionResultText { get; set; } = "";

        public string? LastPath { get; private set; }
        public string? LastSpeechJson { get; private set; }
        public string? LastAuthorization { get; private set; }
        public byte[]? LastTranscriptionBody { get; private set; }

        public void Dispose() => _listener.Close();

        private async Task AcceptLoop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                _ = Task.Run(() => Serve(context));
            }
        }

        private async Task Serve(HttpListenerContext context)
        {
            try
            {
                LastPath = context.Request.Url?.AbsolutePath;
                LastAuthorization = context.Request.Headers["Authorization"];
                if (LastPath == "/v1/audio/speech")
                {
                    using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
                    LastSpeechJson = await reader.ReadToEndAsync();
                    await ServeSpeech(context);
                }
                else if (LastPath == "/v1/audio/transcriptions")
                {
                    using MemoryStream body = new();
                    await context.Request.InputStream.CopyToAsync(body);
                    LastTranscriptionBody = body.ToArray();
                    await ServeTranscription(context);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
            catch
            {
                // 客户端取消，正常
            }
        }

        private async Task ServeSpeech(HttpListenerContext context)
        {
            switch (SpeechMode)
            {
                case EMode.Bytes:
                case EMode.Raw:
                    context.Response.ContentType = SpeechMode == EMode.Raw ? "audio/mpeg" : "audio/wav";
                    await context.Response.OutputStream.WriteAsync(BytesPayload);
                    context.Response.Close();
                    return;

                case EMode.Error:
                    context.Response.StatusCode = ErrorStatus;
                    byte[] errorBytes = Encoding.UTF8.GetBytes(ErrorBody);
                    await context.Response.OutputStream.WriteAsync(errorBytes);
                    context.Response.Close();
                    return;

                case EMode.Sse:
                    context.Response.ContentType = "text/event-stream";
                    await using (context.Response.OutputStream)
                    {
                        foreach (string delta in SseDeltas)
                        {
                            await WriteSse(context.Response.OutputStream, $"{{\"type\":\"speech.audio.delta\",\"audio\":\"{delta}\"}}");
                            if (SseGapMs > 0) await Task.Delay(SseGapMs);
                        }

                        if (SseErrorEvent)
                        {
                            await WriteSse(context.Response.OutputStream, "{\"type\":\"error\",\"message\":\"bad thing happened\"}");
                        }
                        else
                        {
                            await WriteSse(context.Response.OutputStream, "{\"type\":\"speech.audio.done\",\"timing\":{}}");
                        }

                        await WriteSse(context.Response.OutputStream, "[DONE]");
                    }

                    return;
            }
        }

        private static async Task WriteSse(Stream output, string data)
        {
            byte[] bytes = Encoding.UTF8.GetBytes($"data: {data}\n\n");
            await output.WriteAsync(bytes);
            await output.FlushAsync();
        }

        private async Task ServeTranscription(HttpListenerContext context)
        {
            string json = $"{{\"text\":\"{TranscriptionResultText}\"}}";
            context.Response.ContentType = "application/json";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private static int FreePort()
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
