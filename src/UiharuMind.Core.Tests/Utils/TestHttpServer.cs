using System.Net;
using System.Net.Sockets;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 本机 HTTP 文件服务：可关掉 Range 支持、可限速、统计实际送出的字节
/// </summary>
internal sealed class TestHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly byte[] _payload;
    private long _bytesServed;
    private int _rangeRequests;

    public TestHttpServer(byte[] payload)
    {
        _payload = payload;
        int port = FreePort();
        Url = new Uri($"http://127.0.0.1:{port}/file.bin");
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public Uri Url { get; }
    public bool SupportsRange { get; init; } = true;
    public int StatusCode { get; set; } = 200;
    public int BytesPerSecond { get; set; }
    public long BytesServed => Interlocked.Read(ref _bytesServed);
    public int RangeRequests => _rangeRequests;

    public void Dispose()
    {
        _listener.Close();
    }

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
            if (StatusCode != 200)
            {
                context.Response.StatusCode = StatusCode;
                context.Response.Close();
                return;
            }

            long from = 0;
            long to = _payload.Length - 1;
            string? range = context.Request.Headers["Range"];
            if (SupportsRange && range != null)
            {
                Interlocked.Increment(ref _rangeRequests);
                string[] parts = range["bytes=".Length..].Split('-');
                from = long.Parse(parts[0]);
                if (parts[1].Length > 0) to = Math.Min(to, long.Parse(parts[1]));
                context.Response.StatusCode = 206;
                context.Response.Headers["Content-Range"] = $"bytes {from}-{to}/{_payload.Length}";
            }

            context.Response.ContentLength64 = to - from + 1;
            Stream output = context.Response.OutputStream;
            for (long offset = from; offset <= to;)
            {
                int chunk = (int)Math.Min(64 * 1024, to - offset + 1);
                await output.WriteAsync(_payload.AsMemory((int)offset, chunk));
                Interlocked.Add(ref _bytesServed, chunk);
                offset += chunk;
                if (BytesPerSecond > 0) await Task.Delay(chunk * 1000 / BytesPerSecond);
            }

            context.Response.Close();
        }
        catch
        {
            // 客户端取消时连接被掐，正常
        }
    }

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
