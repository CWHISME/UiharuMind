using System.Net;
using System.Text.Json;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>
/// models.dev 数据的运行时来源：读本地快照、按需联网、手动刷新。
///
/// <para><b>不设 TTL、不做定时刷新</b>（ADR 0103）：联网只发生在两处——打开模型窗口发现本地为空，
/// 或用户点「刷新」。启动只读本地文件，一次请求都不发，保住「启动零网络」。</para>
///
/// <para>读缓存与联网必须分开：<see cref="ModelCapabilityResolver"/> 在<b>发消息时</b>也要被调用
/// （决定要不要丢图），那时用户可能几个月没开过模型窗口。只在窗口里读，运行时就永远拿不到动态数据。</para>
///
/// <para>数据缺失不是错误：<see cref="Current"/> 为空时所有查询都 false，调用方退回构建期的静态表。</para>
/// </summary>
public sealed class ModelsDevCatalog
{
    /// <summary>投影格式版本；改动字段含义时 +1，旧缓存文件会被丢弃重拉</summary>
    public const int SchemaVersion = 1;

    private const string ApiUrl = "https://models.dev/api.json";
    private const string CacheFileName = "snapshot.json";

    private static readonly HttpClient Http = CreateClient();

    private readonly string _cachePath;
    private readonly object _sync = new();
    private ModelsDevSnapshot _current = ModelsDevSnapshot.Empty;
    private ModelsDevCacheFile? _file; //最近一次读写的落盘形态，304 时只改 fetchedAt 再写回
    private Task<bool>? _inflight; //单飞：两个窗口同时要，只发一个请求

    /// <summary>进程内唯一实例</summary>
    public static ModelsDevCatalog Instance { get; } = new(Path.Combine(AppPaths.Cache.ModelsDev, CacheFileName));

    /// <summary>
    /// 建一个落在指定文件的实例（测试与备用档案用）
    /// </summary>
    /// <param name="cachePath">快照文件路径</param>
    public ModelsDevCatalog(string cachePath)
    {
        _cachePath = cachePath;
    }

    /// <summary>当前快照；空快照表示还没拉到，查询一律 false，不抛异常</summary>
    internal ModelsDevSnapshot Current => Volatile.Read(ref _current);

    /// <summary>可用供应商（已滤掉无地址与模板地址的），按名称排序</summary>
    public IReadOnlyList<RemoteProviderInfo> Providers => Current.Providers;

    /// <summary>本地是否已有数据（决定界面显示列表还是加载态）</summary>
    public bool IsLoaded => !Current.IsEmpty;

    /// <summary>这份数据的拉取时间，界面上的「3 天前更新」用它；没拉过为 null</summary>
    public DateTimeOffset? FetchedAt => Current.FetchedAt;

    /// <summary>
    /// 按 models.dev 的 provider 键找一家（编辑重开时还原身份条用）。
    /// 线性扫一遍：两百来条，比维护一张索引省事
    /// </summary>
    /// <param name="providerId">provider 键</param>
    /// <param name="provider">找到的供应商</param>
    public bool TryGetProvider(string providerId, out RemoteProviderInfo provider)
    {
        foreach (RemoteProviderInfo item in Current.Providers)
        {
            if (!string.Equals(item.Id, providerId, StringComparison.OrdinalIgnoreCase)) continue;
            provider = item;
            return true;
        }

        provider = null!;
        return false;
    }

    /// <summary>快照换代（含 304 只更新时间的场景），界面据此重查价格与能力</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 启动时读一次本地快照。<b>不联网</b>，失败静默（当没数据，等打开模型窗口时再拉）。
    /// </summary>
    public Task LoadCacheAsync() => Task.Run(LoadCache);

    /// <summary>
    /// 本地有数据就直接返回；没有才联网拉一次。打开模型窗口时调，幂等且单飞。
    /// </summary>
    /// <returns>最终是否有可用数据</returns>
    public Task<bool> EnsureAsync() => IsLoaded ? Task.FromResult(true) : StartFetch(conditional: false);

    /// <summary>
    /// 手动刷新：无条件走一次条件请求（带 ETag），没变就只更新时间戳。
    /// </summary>
    /// <returns>是否成功拿到（含 304）</returns>
    public Task<bool> RefreshAsync() => StartFetch(conditional: true);

    private void LoadCache()
    {
        ModelsDevCacheFile? file = ReadCacheFile(_cachePath);
        if (file is null) return;

        ModelsDevSnapshot snapshot = ModelsDevSnapshot.FromCacheFile(file);
        lock (_sync)
        {
            _file = file;
            Volatile.Write(ref _current, snapshot);
        }

        Log.Debug($"[ModelsDev] cache loaded: {snapshot.Providers.Count} providers, fetched at {file.FetchedAt:u}");
        RaiseChanged();
    }

    private Task<bool> StartFetch(bool conditional)
    {
        lock (_sync)
        {
            if (_inflight is { IsCompleted: false }) return _inflight;
            _inflight = FetchCoreAsync(conditional);
            return _inflight;
        }
    }

    private async Task<bool> FetchCoreAsync(bool conditional)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, ApiUrl);
            string? etag = Current.ETag;
            if (conditional && etag is { Length: > 0 })
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using HttpResponseMessage response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                TouchFetchedAt();
                return true;
            }

            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            Dictionary<string, ModelsDevRawProvider>? raw = await JsonSerializer
                .DeserializeAsync(stream, ModelsDevJsonContext.Default.DictionaryStringModelsDevRawProvider)
                .ConfigureAwait(false);

            if (raw is null || raw.Count == 0)
            {
                Log.Warning("[ModelsDev] api.json parsed empty, keeping previous snapshot");
                return false;
            }

            // 用 ToString() 而不是 Tag：models.dev 回的是弱校验器 W/"…"，剥掉前缀发回去就不是同一个值了
            string? newEtag = response.Headers.ETag?.ToString();
            ModelsDevCacheFile file = ModelsDevProjection.Build(raw, DateTimeOffset.Now, newEtag);
            ModelsDevSnapshot snapshot = ModelsDevSnapshot.FromCacheFile(file);

            SaveCacheFile(_cachePath, file);
            lock (_sync)
            {
                _file = file;
                Volatile.Write(ref _current, snapshot);
            }

            Log.Debug($"[ModelsDev] fetched: {snapshot.Providers.Count} providers, {raw.Count} in payload");
            RaiseChanged();
            return true;
        }
        catch (Exception e)
        {
            // 失败不改现状：有旧快照就继续用旧的，没有就保持空
            Log.Warning($"[ModelsDev] fetch failed: {e.Message}");
            return false;
        }
        finally
        {
            lock (_sync)
            {
                _inflight = null;
            }
        }
    }

    /// <summary>304：数据没变，只把「上次检查时间」往前挪，界面上的数据年龄才说得过去</summary>
    private void TouchFetchedAt()
    {
        ModelsDevCacheFile? file;
        lock (_sync)
        {
            file = _file;
            if (file is null) return;
            file.FetchedAt = DateTimeOffset.Now;
        }

        SaveCacheFile(_cachePath, file);
        ModelsDevSnapshot snapshot = ModelsDevSnapshot.FromCacheFile(file);
        Volatile.Write(ref _current, snapshot);
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static ModelsDevCacheFile? ReadCacheFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            ModelsDevCacheFile? file = JsonSerializer.Deserialize(stream, ModelsDevJsonContext.Default.ModelsDevCacheFile);
            if (file is null) return null;
            if (file.SchemaVersion != SchemaVersion)
            {
                Log.Debug($"[ModelsDev] cache schema {file.SchemaVersion} != {SchemaVersion}, ignored");
                return null;
            }

            return file;
        }
        catch (Exception e)
        {
            Log.Warning($"[ModelsDev] cache read failed: {e.Message}");
            return null;
        }
    }

    private static void SaveCacheFile(string path, ModelsDevCacheFile file)
    {
        try
        {
            string json = JsonSerializer.Serialize(file, ModelsDevJsonContext.Default.ModelsDevCacheFile);
            SaveUtility.SaveText(path, json);
        }
        catch (Exception e)
        {
            // 存不下不影响本次使用：内存里已经有快照了
            Log.Warning($"[ModelsDev] cache write failed: {e.Message}");
        }
    }

    private static HttpClient CreateClient()
    {
        SocketsHttpHandler handler = new()
        {
            // 原始 5.7 MB，brotli 后约 0.5 MB——服务端支持，就别省这一步
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        };
        HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UiharuMind/1.0");
        return client;
    }

    /// <summary>测试用：直接换一份快照，跳过网络与磁盘</summary>
    /// <param name="snapshot">要装上的快照</param>
    internal void Install(ModelsDevSnapshot snapshot)
    {
        Volatile.Write(ref _current, snapshot);
        RaiseChanged();
    }
}
