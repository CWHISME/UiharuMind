using System.Text.Json;
using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 本机还没有这个任务能用的音频模型
/// </summary>
/// <param name="task">任务</param>
public sealed class AudioModelMissingException(EAudioCppTask task)
    : Exception($"No local audio model for '{task.ToConfigName()}' is installed.")
{
    /// <summary>
    /// 缺模型的任务
    /// </summary>
    public EAudioCppTask Task { get; } = task;
}

/// <summary>
/// audio.cpp 本地引擎，由三块拼成：分发（版本与安装，<see cref="Distribution"/>）、模型（<see cref="Models"/>）、
/// 进程（按设置生成 server.json 并托管，<see cref="Runtime"/>）。
/// 设置与模型目录是唯一事实来源，进程只是它们的投影——选了没在跑的模型、换了版本或改了启动项，下次用时重启一次
/// </summary>
public sealed partial class AudioCppEngine : IDisposable
{
    private static readonly HttpClient StatusClient = new() { Timeout = TimeSpan.FromSeconds(3) }; //查状态用，本机回环
    private static readonly Lazy<AudioCppEngine> SharedInstance = new(() => new AudioCppEngine(
        AudioCppSettingConfig.Current,
        Path.Combine(AppPaths.External.Engine, "AudioCpp"),
        Path.Combine(AppPaths.Cache.Scratch, $"audiocpp-server-{Environment.ProcessId}.json"),
        () => AudioCppModels.FromLocal(LocalModelScanner.Scan()),
        VoiceLibrary.Shared));

    private readonly AudioCppSettingConfig _settings;
    private readonly string _configPath;
    private readonly LocalServerHost _host;
    private readonly Lock _gate = new();
    private string? _launchedBackend; //最近一次启动用的后端
    private IReadOnlyList<AudioCppModelEntry> _launchedModels = [];

    /// <param name="settings">引擎设置</param>
    /// <param name="engineRoot">引擎包目录</param>
    /// <param name="configPath">server.json 写到哪</param>
    /// <param name="scanModels">扫描本地音频模型</param>
    /// <param name="voices">音色库，作为 server 的 voice_dir；null 不配</param>
    /// <param name="queue">下载队列；null 用全局的</param>
    public AudioCppEngine(
        AudioCppSettingConfig settings,
        string engineRoot,
        string configPath,
        Func<IReadOnlyList<AudioCppModelEntry>> scanModels,
        VoiceLibrary? voices = null,
        DownloadQueue? queue = null)
    {
        Voices = voices;
        _settings = settings;
        _configPath = configPath;
        _host = new LocalServerHost(CreateLaunch);
        Distribution = new EngineDistribution<AudioCppVersion>("audio.cpp", engineRoot, new AudioCppVersionManager(),
            queue ?? DownloadQueue.Shared, settings, ReleaseUrl);
        Models = new AudioCppModelResolver(settings, () => Distribution.CurrentVersion, scanModels);
        // 先换再重启：spec 随版本换，在跑的进程换成新版本
        Distribution.VersionChanged += () =>
        {
            Models.OnVersionChanged();
            _host.Stop();
        };
    }

    /// <summary>
    /// 走应用档案的那一个
    /// </summary>
    public static AudioCppEngine Shared => SharedInstance.Value;

    /// <summary>
    /// 版本与安装
    /// </summary>
    public EngineDistribution<AudioCppVersion> Distribution { get; }

    /// <summary>
    /// 本机的语音模型与在用引擎的 spec
    /// </summary>
    public AudioCppModelResolver Models { get; }

    /// <summary>
    /// 进程在不在跑、停掉释放内存
    /// </summary>
    public IEngineRuntime Runtime => _host;

    /// <summary>
    /// 引擎设置
    /// </summary>
    public AudioCppSettingConfig Settings => _settings;

    /// <summary>
    /// 音色库；没配为 null
    /// </summary>
    public VoiceLibrary? Voices { get; }

    /// <summary>
    /// 停掉引擎，释放模型占用的内存；下次使用时自动再拉起
    /// </summary>
    public void Stop() => _host.Stop();

    /// <summary>
    /// 能服务这个模型的 server 地址；没在跑就拉起，在跑但没登记这个模型就重启
    /// </summary>
    /// <param name="model">要用的模型</param>
    /// <param name="cancellationToken">只取消这次等待</param>
    /// <returns>服务根地址</returns>
    /// <exception cref="LocalEngineNotReadyException">没装引擎</exception>
    /// <exception cref="LocalServerException">起不来</exception>
    public async Task<Uri> GetServerAsync(AudioCppModelEntry model, CancellationToken cancellationToken)
    {
        AudioCppVersion version = await Distribution.EnsureCurrentAsync(cancellationToken).ConfigureAwait(false)
                                  ?? throw new LocalEngineNotReadyException();

        bool stale;
        lock (_gate) stale = _launchedModels.Count > 0 && !_launchedModels.Contains(model);
        if (stale) _host.Stop();

        // 启动项（线程、驻留数…）改了，指纹跟着变，宿主下次用时换进程
        string launchKey = $"{version.Name}\n{BuildServerOptions(version.Name, _settings, Voices?.Directory)}";
        return await _host.GetBaseUriAsync(launchKey, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 跑着的引擎用什么后端、哪些模型已加载。只在已经跑着时去问服务，不会为了查状态把引擎拉起来
    /// </summary>
    /// <param name="cancellationToken">取消</param>
    /// <returns>状态</returns>
    public async Task<AudioCppEngineStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (_host.RunningBaseUri is not { } baseUri) return AudioCppEngineStatus.Stopped;
        HashSet<string> loaded = new(StringComparer.Ordinal);
        try
        {
            using HttpResponseMessage response = await StatusClient.GetAsync(new Uri(baseUri, "v1/models"), cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                foreach (JsonElement model in data.EnumerateArray())
                    if (model.TryGetProperty("loaded", out JsonElement isLoaded) && isLoaded.ValueKind == JsonValueKind.True &&
                        model.TryGetProperty("id", out JsonElement id) && id.GetString() is { } name)
                        loaded.Add(name);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Log.Debug($"Query audio.cpp status failed: {e.Message}");
        }

        return new AudioCppEngineStatus(_launchedBackend, loaded);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    // 宿主在锁外调这里，可以放心取引擎自己的锁、扫模型目录
    private LocalServerLaunch CreateLaunch()
    {
        AudioCppVersion version = Distribution.CurrentVersion ?? throw new LocalEngineNotReadyException();
        IReadOnlyList<AudioCppModelEntry> models = Models.GetModels(true);
        lock (_gate) _launchedModels = models;

        string exe = version.GetExecutable(AudioCppVersionManager.ExecutableName);
        if (Voices != null) Directory.CreateDirectory(Voices.Directory);
        AudioCppServerOptions options = BuildServerOptions(version.Name, _settings, Voices?.Directory);
        _launchedBackend = options.Backend;
        return AudioCppServerConfig.Launch(exe, _configPath, models, options,
            TimeSpan.FromSeconds(Math.Max(10, _settings.ReadyTimeoutSeconds)));
    }

    /// <summary>
    /// 从版本名里认出发布号（audio-v0.9.1-bin-… 里的 v0.9.1），拼 GitHub 发布页
    /// </summary>
    /// <param name="versionName">版本名</param>
    /// <returns>发布页；认不出为 null</returns>
    public static string? ReleaseUrl(string versionName)
    {
        Match match = TagPattern().Match(versionName);
        return match.Success ? $"https://github.com/0xShug0/audio.cpp/releases/tag/{match.Groups[1].Value}" : null;
    }

    /// <summary>
    /// 设置 → server.json 顶层选项：后端随引擎包变体，设置可强制 CPU；数值兜住下限
    /// </summary>
    internal static AudioCppServerOptions BuildServerOptions(string packageName, AudioCppSettingConfig settings,
        string? voiceDir = null) => new(
        settings.ForceCpu ? "cpu" : AudioCppVariants.GetBackend(packageName),
        Math.Max(1, settings.Threads),
        Math.Max(1, settings.MaxLoadedModels),
        Math.Max(1, settings.IdleUnloadMinutes) * 60_000,
        Math.Max(0, settings.MinFreeMemoryMb),
        voiceDir);

    [GeneratedRegex(@"^audio-(.+?)-bin-")]
    private static partial Regex TagPattern();
}
