using System.Net;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Net;

public class HttpProxyServer
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private readonly Func<HttpListenerRequest, Uri> _uriGenerator;
    private readonly Func<string, string> _responseContentProcessor;
    private readonly HttpListener _listener;
    private bool _isRunning;

    public HttpProxyServer(string urlPrefix, Func<HttpListenerRequest, Uri> uriGenerator,
        Func<string, string> responseContentProcessor)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(urlPrefix);
        _uriGenerator = uriGenerator;
        _responseContentProcessor = responseContentProcessor;
    }

    public void Start()
    {
        if (!_isRunning) Task.Run(StartAsync);
    }

    public async Task StartAsync()
    {
        if (_isRunning || _listener.IsListening) return;
        _listener.Start();
        _isRunning = true;
        Log.Debug($"Proxy started at {string.Join(", ", _listener.Prefixes)}");

        while (_isRunning)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                await Task.Run(() => ProcessRequest(context));
            }
            catch (Exception ex)
            {
                Log.Debug($"Error: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _listener.Stop();
        _listener.Close();
        Log.Debug("Proxy stopped.");
    }

    private async Task ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        Log.Debug($"Request received: {request.HttpMethod} {request.RawUrl}");
        try
        {
            var targetUri = _uriGenerator(request);
            var httpRequest = new HttpRequestMessage
            {
                RequestUri = new Uri(targetUri, request.RawUrl),
                Method = new HttpMethod(request.HttpMethod)
            };

            // Copy headers
            foreach (var headerKey in request.Headers.AllKeys)
            {
                if (headerKey == null || headerKey == "Host") continue;
                httpRequest.Headers.TryAddWithoutValidation(headerKey, request.Headers[headerKey]);
            }

            if (request.HasEntityBody)
            {
                using var contentStream = new MemoryStream();
                await request.InputStream.CopyToAsync(contentStream);
                httpRequest.Content = new ByteArrayContent(contentStream.ToArray());
            }

            // 发送请求
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var httpResponse = await HttpClient.SendAsync(httpRequest, timeoutCts.Token);

            // 设置响应状态和头
            context.Response.StatusCode = (int)httpResponse.StatusCode;
            foreach (var header in httpResponse.Headers)
            {
                foreach (var value in header.Value)
                {
                    context.Response.Headers[header.Key] = value;
                }
            }

            byte[] responseBodyBytes = await httpResponse.Content.ReadAsByteArrayAsync(timeoutCts.Token);
            string responseBodyContent = ProcessResponseContent(responseBodyBytes);
            responseBodyBytes = System.Text.Encoding.UTF8.GetBytes(responseBodyContent);
            context.Response.ContentLength64 = responseBodyBytes.Length;
            await using var writer = new BinaryWriter(context.Response.OutputStream);
            writer.Write(responseBodyBytes);
        }
        catch (TaskCanceledException)
        {
            context.Response.StatusCode = 504; // Gateway Timeout
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = 502; // Bad Gateway
        }
        finally
        {
            context.Response.Close();
        }
    }

    private string ProcessResponseContent(byte[] contentBytes)
    {
        string content = System.Text.Encoding.UTF8.GetString(contentBytes);
        var processedContent = _responseContentProcessor(content);
        Log.Debug($"Processed by Proxy Server: {processedContent}");
        return processedContent;
    }
}