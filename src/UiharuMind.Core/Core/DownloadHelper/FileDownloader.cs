using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.DownloadHelper;

/// <summary>
/// 一次下载请求
/// </summary>
/// <param name="Url">下载地址</param>
/// <param name="DestinationPath">最终文件路径</param>
/// <param name="Segments">分块并发数；GitHub 这类单连接限速的源开多块，镜像站用 1</param>
/// <param name="ExpectedSha256">期望的 sha256（十六进制），为空不校验</param>
/// <param name="Headers">额外请求头（如 Authorization）</param>
public sealed record DownloadRequest(
    Uri Url,
    string DestinationPath,
    int Segments = 1,
    string? ExpectedSha256 = null,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// 下载进度
/// </summary>
/// <param name="ReceivedBytes">已收字节（含续传前已有的）</param>
/// <param name="TotalBytes">总字节，未知为 null</param>
/// <param name="BytesPerSecond">近一段时间的速度</param>
public readonly record struct DownloadProgress(long ReceivedBytes, long? TotalBytes, double BytesPerSecond);

/// <summary>
/// 下完的文件大小或校验值对不上，已删掉重来
/// </summary>
public sealed class DownloadVerificationException(string message) : Exception(message);

/// <summary>
/// 断点续传下载器：下到 &lt;目标&gt;.part，旁挂 &lt;目标&gt;.part.json 记地址与各块进度，重启后接着下；
/// 地址变了（如换了模型源）从头下，不拼两边的数据。单块时边下边算 sha256，多块时下完再算。
/// </summary>
public sealed class FileDownloader(HttpClient httpClient)
{
    private const long MinSegmentBytes = 1L << 20;
    private const int BufferBytes = 1 << 16;
    private const long StateSaveIntervalBytes = 4L << 20;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 走系统代理、不限总时长（大文件），卡住由读超时兜底
    /// </summary>
    public static FileDownloader Shared { get; } = new(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

    /// <summary>
    /// 下载到目标路径；目标已存在视为已完成
    /// </summary>
    /// <param name="request">下载请求</param>
    /// <param name="progress">进度回调（节流到 200ms）</param>
    /// <param name="cancellationToken">取消即暂停，已下部分保留</param>
    /// <exception cref="DownloadVerificationException">大小或校验值不符</exception>
    public async Task DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(request.DestinationPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.DestinationPath))!);

        string partPath = request.DestinationPath + ".part";
        DownloadState? state = DownloadState.Load(partPath);
        if (state == null || state.Url != request.Url.ToString() || !File.Exists(partPath))
        {
            DeletePartial(partPath);
            state = await ProbeAsync(request, cancellationToken).ConfigureAwait(false);
            if (state != null)
            {
                await using (FileStream file = new(partPath, FileMode.Create, FileAccess.Write))
                    file.SetLength(state.TotalBytes);
                state.Save(partPath);
            }
        }

        ProgressMeter meter = new(progress, state?.TotalBytes);
        string? sha256;
        if (state == null)
            sha256 = await DownloadWholeAsync(request, partPath, meter, cancellationToken).ConfigureAwait(false);
        else
            sha256 = await DownloadRangesAsync(request, partPath, state, meter, cancellationToken).ConfigureAwait(false);
        meter.Report(force: true);

        if (!string.IsNullOrEmpty(request.ExpectedSha256))
        {
            sha256 ??= await HashFileAsync(partPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sha256, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                DeletePartial(partPath);
                throw new DownloadVerificationException(
                    $"sha256 mismatch for {Path.GetFileName(request.DestinationPath)}: expected {request.ExpectedSha256}, got {sha256}.");
            }
        }

        File.Move(partPath, request.DestinationPath, true);
        File.Delete(DownloadState.PathFor(partPath));
    }

    /// <summary>
    /// 删掉未下完的部分（取消下载时用）
    /// </summary>
    /// <param name="destinationPath">最终文件路径</param>
    public static void DeletePartial(string destinationPath)
    {
        string partPath = destinationPath.EndsWith(".part", StringComparison.Ordinal)
            ? destinationPath
            : destinationPath + ".part";
        // 还没开下的项连目录都可能没有，File.Delete 遇到缺目录会抛
        if (!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(partPath)))) return;
        File.Delete(partPath);
        File.Delete(DownloadState.PathFor(partPath));
    }

    /// <summary>
    /// 读未下完文件的进度（重启后列状态用）
    /// </summary>
    /// <param name="destinationPath">最终文件路径</param>
    /// <returns>已下与总字节；没有可续传的进度为 null</returns>
    public static DownloadProgress? ReadPartialProgress(string destinationPath)
    {
        string partPath = destinationPath + ".part";
        if (!File.Exists(partPath)) return null;
        DownloadState? state = DownloadState.Load(partPath);
        return state == null ? null : new DownloadProgress(state.Segments.Sum(x => x.Done), state.TotalBytes, 0);
    }

    // 用 bytes=0-0 探一下：206 + Content-Range 才能分块续传；否则整个下，不能续传
    private async Task<DownloadState?> ProbeAsync(DownloadRequest request, CancellationToken token)
    {
        using HttpRequestMessage message = CreateRequest(request, 0, 0);
        using HttpResponseMessage response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentRange?.Length;
        if (response.StatusCode != HttpStatusCode.PartialContent || total is not > 0) return null;

        int segments = (int)Math.Clamp(total.Value / MinSegmentBytes, 1, Math.Max(1, request.Segments));
        return DownloadState.Create(request.Url.ToString(), total.Value, segments);
    }

    private async Task<string> DownloadWholeAsync(
        DownloadRequest request, string partPath, ProgressMeter meter, CancellationToken token)
    {
        using HttpRequestMessage message = CreateRequest(request, null, null);
        using HttpResponseMessage response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        meter.SetTotal(response.Content.Headers.ContentLength);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using FileStream file = new(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes,
            true);
        byte[] buffer = new byte[BufferBytes];
        while (true)
        {
            int read = await ReadWithIdleTimeoutAsync(body, buffer, token).ConfigureAwait(false);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            meter.Add(read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // 返回单块时边下边算的 sha256，多块返回 null 交给调用方下完再算
    private async Task<string?> DownloadRangesAsync(
        DownloadRequest request, string partPath, DownloadState state, ProgressMeter meter, CancellationToken token)
    {
        meter.Add(state.Segments.Sum(x => x.Done));
        IncrementalHash? hash = null;
        try
        {
            using SafeFileHandle handle = File.OpenHandle(partPath, FileMode.Open, FileAccess.ReadWrite,
                FileShare.None, FileOptions.Asynchronous);
            if (state.Segments.Count == 1)
            {
                hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await SeedHashAsync(handle, hash, state.Segments[0].Done, token).ConfigureAwait(false);
            }

            Lock stateGate = new();
            await Task.WhenAll(state.Segments.Select(segment =>
                    DownloadSegmentWithRetryAsync(request, handle, segment, hash, meter, () =>
                    {
                        RandomAccess.FlushToDisk(handle);
                        lock (stateGate) state.Save(partPath);
                    }, token)))
                .ConfigureAwait(false);
            RandomAccess.FlushToDisk(handle);
            state.Save(partPath);
            return hash == null ? null : Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            hash?.Dispose();
        }
    }

    // 网络抖动时同一块重试几次，接着已写的位置下；多块下载里一块失败不拖累其余已下的进度
    private async Task DownloadSegmentWithRetryAsync(
        DownloadRequest request,
        SafeFileHandle handle,
        DownloadSegment segment,
        IncrementalHash? hash,
        ProgressMeter meter,
        Action checkpoint,
        CancellationToken token)
    {
        for (int attempt = 1;; attempt++)
        {
            try
            {
                await DownloadSegmentAsync(request, handle, segment, hash, meter, checkpoint, token)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception e) when (attempt < MaxAttempts && !token.IsCancellationRequested &&
                                      e is HttpRequestException or IOException or TimeoutException)
            {
                Log.Warning($"Download segment retry {attempt}: {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), token).ConfigureAwait(false);
            }
        }
    }

    private async Task DownloadSegmentAsync(
        DownloadRequest request,
        SafeFileHandle handle,
        DownloadSegment segment,
        IncrementalHash? hash,
        ProgressMeter meter,
        Action checkpoint,
        CancellationToken token)
    {
        if (segment.IsComplete) return;

        using HttpRequestMessage message = CreateRequest(request, segment.Start + segment.Done, segment.End);
        using HttpResponseMessage response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new HttpRequestException("Server stopped honoring range requests.");

        await using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] buffer = new byte[BufferBytes];
        long sinceCheckpoint = 0;
        try
        {
            while (!segment.IsComplete)
            {
                int read = await ReadWithIdleTimeoutAsync(body, buffer, token).ConfigureAwait(false);
                if (read == 0) throw new IOException("Connection closed before the range was complete.");
                read = (int)Math.Min(read, segment.Remaining);
                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), segment.Start + segment.Done, token)
                    .ConfigureAwait(false);
                hash?.AppendData(buffer, 0, read);
                segment.Done += read;
                meter.Add(read);
                sinceCheckpoint += read;
                if (sinceCheckpoint < StateSaveIntervalBytes) continue;
                checkpoint();
                sinceCheckpoint = 0;
            }
        }
        finally
        {
            // 中断时把已写进度落盘，下次从这里接着下
            checkpoint();
        }
    }

    private static async Task SeedHashAsync(SafeFileHandle handle, IncrementalHash hash, long length,
        CancellationToken token)
    {
        byte[] buffer = new byte[1 << 20];
        for (long offset = 0; offset < length;)
        {
            int read = await RandomAccess.ReadAsync(handle,
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - offset)), offset, token).ConfigureAwait(false);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            offset += read;
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
    }

    private static async Task<int> ReadWithIdleTimeoutAsync(Stream body, byte[] buffer, CancellationToken token)
    {
        using CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(IdleTimeout);
        try
        {
            return await body.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"No data received for {IdleTimeout.TotalSeconds:0}s.");
        }
    }

    private static HttpRequestMessage CreateRequest(DownloadRequest request, long? from, long? to)
    {
        HttpRequestMessage message = new(HttpMethod.Get, request.Url);
        if (from != null) message.Headers.Range = new RangeHeaderValue(from, to);
        if (request.Headers == null) return message;
        foreach ((string key, string value) in request.Headers)
            message.Headers.TryAddWithoutValidation(key, value);
        return message;
    }

    private sealed class ProgressMeter(IProgress<DownloadProgress>? progress, long? total)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _received;
        private long _lastReceived;
        private TimeSpan _lastReport;
        private long? _total = total;

        public void SetTotal(long? value) => _total = value;

        public void Add(long bytes)
        {
            Interlocked.Add(ref _received, bytes);
            Report(force: false);
        }

        public void Report(bool force)
        {
            if (progress == null) return;
            TimeSpan now = _clock.Elapsed;
            TimeSpan elapsed = now - _lastReport;
            if (!force && elapsed < ProgressInterval) return;

            lock (_clock)
            {
                if (!force && now - _lastReport < ProgressInterval) return;
                long received = Interlocked.Read(ref _received);
                double speed = elapsed.TotalSeconds > 0 ? (received - _lastReceived) / elapsed.TotalSeconds : 0;
                _lastReceived = received;
                _lastReport = now;
                progress.Report(new DownloadProgress(received, _total, speed));
            }
        }
    }
}
