using System.Text.Json;
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
/// audio.cpp 本地引擎：版本与安装、选中的模型、按设置生成 server.json 并托管进程。
/// 设置与模型目录是唯一事实来源，进程只是它们的投影——选了没在跑的模型、换了版本或改了启动项，就重启一次
/// </summary>
public sealed class AudioCppEngine : IDisposable
{
    private static readonly HttpClient StatusClient = new() { Timeout = TimeSpan.FromSeconds(3) }; //查状态用，本机回环
    private static readonly Lazy<AudioCppEngine> SharedInstance = new(() => new AudioCppEngine(
        AudioCppSettingConfig.Current,
        Path.Combine(AppPaths.External.Engine, "AudioCpp"),
        Path.Combine(AppPaths.Cache.Scratch, $"audiocpp-server-{Environment.ProcessId}.json"),
        () => AudioCppModels.FromLocal(LocalModelScanner.Scan()),
        VoiceLibrary.Shared));

    private readonly AudioCppSettingConfig _settings;
    private readonly string _engineRoot;
    private readonly string _configPath;
    private readonly Func<IReadOnlyList<AudioCppModelEntry>> _scanModels;
    private readonly AudioCppVersionManager _versionManager = new();
    private readonly AudioCppEngineHost _host;
    private readonly Lock _gate = new();
    private IReadOnlyList<AudioCppModelEntry>? _models; //扫描缓存
    private IReadOnlyList<AudioCppFamily>? _families; //在用引擎的 spec，换版本时清
    private string? _launchedBackend; //最近一次启动用的后端
    private IReadOnlyList<AudioCppModelEntry> _launchedModels = [];

    /// <param name="settings">引擎设置</param>
    /// <param name="engineRoot">引擎包目录</param>
    /// <param name="configPath">server.json 写到哪</param>
    /// <param name="scanModels">扫描本地音频模型</param>
    /// <param name="voices">音色库，作为 server 的 voice_dir；null 不配</param>
    public AudioCppEngine(
        AudioCppSettingConfig settings,
        string engineRoot,
        string configPath,
        Func<IReadOnlyList<AudioCppModelEntry>> scanModels,
        VoiceLibrary? voices = null)
    {
        Voices = voices;
        _settings = settings;
        _engineRoot = engineRoot;
        _configPath = configPath;
        _scanModels = scanModels;
        _host = new AudioCppEngineHost(CreateLaunch);
        Installer = new EngineInstaller<AudioCppVersion>("audio.cpp", DownloadQueue.Shared, engineRoot,
            GetLocalVersionsAsync, PullLatestVersionsAsync, InstallArchiveAsync, SelectVersion);
    }

    /// <summary>
    /// 走应用档案的那一个
    /// </summary>
    public static AudioCppEngine Shared => SharedInstance.Value;

    /// <summary>
    /// 引擎包目录
    /// </summary>
    public string EngineRoot => _engineRoot;

    /// <summary>
    /// 在用的引擎版本变了（装好、切换、删掉；可能在下载线程触发）。模型目录随引擎走，界面据此重列
    /// </summary>
    public event Action? VersionChanged;

    /// <summary>
    /// 引擎设置
    /// </summary>
    public AudioCppSettingConfig Settings => _settings;

    /// <summary>
    /// 音色库；没配为 null
    /// </summary>
    public VoiceLibrary? Voices { get; }

    /// <summary>
    /// 引擎包的下载与安装
    /// </summary>
    public EngineInstaller<AudioCppVersion> Installer { get; }

    /// <summary>
    /// 在用的引擎版本；没装为 null
    /// </summary>
    public AudioCppVersion? CurrentVersion { get; private set; }

    /// <summary>
    /// 最近一次拉到的发布说明；没拉过为 null
    /// </summary>
    public string? ReleaseInfoText => _versionManager.ReleaseInfoText;

    /// <summary>
    /// 本机已装的版本，顺带确定在用的那个
    /// </summary>
    /// <returns>版本列表</returns>
    public async Task<IReadOnlyList<AudioCppVersion>> GetLocalVersionsAsync()
    {
        Directory.CreateDirectory(_engineRoot);
        IReadOnlyList<AudioCppVersion> versions =
            await _versionManager.GetLocalVersionsAsync(_engineRoot).ConfigureAwait(false);
        SetCurrentVersion(versions.FirstOrDefault(x => x.IsInstalled && x.Name == _settings.SelectedRuntimeVersion)
                          ?? versions.FirstOrDefault(x => x.IsInstalled));
        return versions;
    }

    /// <summary>
    /// 拉远端版本（含本机已有的）
    /// </summary>
    /// <returns>版本列表</returns>
    public Task<IReadOnlyList<AudioCppVersion>> PullLatestVersionsAsync()
    {
        Directory.CreateDirectory(_engineRoot);
        return _versionManager.PullLatestVersionsAsync(_engineRoot);
    }

    /// <summary>
    /// 解压校验一个已下好的包，成功后删掉压缩包
    /// </summary>
    public Task InstallArchiveAsync(AudioCppVersion version, CancellationToken cancellationToken)
    {
        return _versionManager.InstallArchiveAsync(version, true, cancellationToken);
    }

    /// <summary>
    /// 选中一个版本并落盘，在跑的进程会换成它
    /// </summary>
    /// <param name="version">版本；null 为不选</param>
    public void SelectVersion(AudioCppVersion? version)
    {
        UseVersion(version);
        _settings.SelectedRuntimeVersion = version?.Name;
        _settings.Save();
    }

    /// <summary>
    /// 本地的语音模型
    /// </summary>
    /// <param name="refresh">重新扫描模型目录</param>
    /// <returns>模型条目</returns>
    public IReadOnlyList<AudioCppModelEntry> GetModels(bool refresh = false)
    {
        lock (_gate)
        {
            if (refresh || _models == null) _models = _scanModels();
            return _models;
        }
    }

    /// <summary>
    /// 某个任务该用的模型：设置里选的，找不到就第一个。选的模型不在缓存里时重扫一次（可能刚下完）
    /// </summary>
    /// <param name="task">任务</param>
    /// <returns>模型条目</returns>
    /// <exception cref="AudioModelMissingException">一个都没有</exception>
    public AudioCppModelEntry ResolveModel(EAudioCppTask task)
    {
        string? selected = task == EAudioCppTask.Tts ? _settings.TtsModel : _settings.AsrModel;
        string name = task.ToConfigName();
        AudioCppModelEntry? model = Find(GetModels(), selected, name);
        if (model == null || (!string.IsNullOrEmpty(selected) && model.Id != selected))
            model = Find(GetModels(true), selected, name) ?? model;
        return model ?? throw new AudioModelMissingException(task);
    }

    /// <summary>
    /// 按文字语种挑模型：优先设置里选的；它明确不会这种语言（spec 里列了语种却没有它）时，
    /// 换一个本机装了、会这种语言的。都不会就还用选的那个
    /// </summary>
    /// <param name="task">任务</param>
    /// <param name="language">语种代码，如 zh；null 不挑</param>
    /// <returns>模型条目</returns>
    /// <exception cref="AudioModelMissingException">一个都没有</exception>
    public AudioCppModelEntry ResolveModel(EAudioCppTask task, string? language)
    {
        AudioCppModelEntry preferred = ResolveModel(task);
        if (language == null || Speaks(preferred, language) != false) return preferred;
        string name = task.ToConfigName();
        return GetModels().Where(x => x.Task == name).FirstOrDefault(x => Speaks(x, language) == true) ?? preferred;
    }

    /// <summary>
    /// 本机有没有这个任务、会这种语言的模型（spec 没列语种的当作会）
    /// </summary>
    /// <param name="task">任务</param>
    /// <param name="language">语种代码；null 只看有没有</param>
    /// <returns>有为 true</returns>
    public bool HasModelFor(EAudioCppTask task, string? language = null)
    {
        string name = task.ToConfigName();
        return GetModels().Any(x => x.Task == name && (language == null || Speaks(x, language) != false));
    }

    /// <summary>
    /// 引擎包自带的 model_specs 目录；没装引擎为 null
    /// </summary>
    public string? GetSpecsDirectory()
    {
        string? install = CurrentVersion?.InstallDirectory;
        if (string.IsNullOrEmpty(install) || !Directory.Exists(install)) return null;
        return Directory.EnumerateDirectories(install, "model_specs", SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>
    /// 能下的语音模型包（读在用引擎自带的 spec）；没装引擎为空
    /// </summary>
    public IReadOnlyList<AudioCppPackage> GetCatalog()
    {
        string? specs = GetSpecsDirectory();
        return specs == null ? [] : AudioCppModelCatalog.Load(specs);
    }

    /// <summary>
    /// 在用引擎的模型家族（读它自带的 spec）；没装引擎为空
    /// </summary>
    public IReadOnlyList<AudioCppFamily> GetFamilies()
    {
        lock (_gate)
        {
            if (_families != null) return _families;
        }

        string? specs = GetSpecsDirectory();
        IReadOnlyList<AudioCppFamily> families = specs == null ? [] : AudioCppModelCatalog.LoadFamilies(specs);
        lock (_gate) _families = families;
        return families;
    }

    /// <summary>
    /// 一个模型包装好了：重扫模型目录；这个任务还没选过模型就选上它
    /// </summary>
    /// <param name="package">装好的包</param>
    public void OnPackageInstalled(AudioCppPackage package)
    {
        IReadOnlyList<AudioCppModelEntry> models = GetModels(true);
        if (models.All(x => x.Id != package.ModelName)) return;
        bool isTts = package.Task == EAudioCppTask.Tts;
        string? selected = isTts ? _settings.TtsModel : _settings.AsrModel;
        if (!string.IsNullOrEmpty(selected) && models.Any(x => x.Id == selected)) return;
        if (isTts) _settings.TtsModel = package.ModelName;
        else _settings.AsrModel = package.ModelName;
        _settings.Save();
    }

    /// <summary>
    /// 启动相关的设置改了（线程数、驻留数…），下次使用按新设置重启
    /// </summary>
    public void ApplySettings()
    {
        _host.Restart();
    }

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
        if (CurrentVersion == null) await GetLocalVersionsAsync().ConfigureAwait(false);
        if (CurrentVersion == null) throw new LocalEngineNotReadyException();

        // 宿主起进程时持宿主锁再来取引擎锁（CreateLaunch），这里就不能反过来持引擎锁去碰宿主
        bool stale;
        lock (_gate) stale = _launchedModels.Count > 0 && !_launchedModels.Contains(model);
        if (stale) _host.Restart();

        return await _host.GetBaseUriAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 引擎在不在跑、用什么后端、哪些模型已加载。只在已经跑着时去问服务，不会为了查状态把引擎拉起来
    /// </summary>
    /// <param name="cancellationToken">取消</param>
    /// <returns>状态</returns>
    public async Task<AudioCppEngineStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (_host.IsStarting)
            return new AudioCppEngineStatus(EAudioEngineRunState.Starting, _launchedBackend, new HashSet<string>());
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

        return new AudioCppEngineStatus(EAudioEngineRunState.Running, _launchedBackend, loaded);
    }

    /// <summary>
    /// 停掉引擎，释放模型占用的内存；下次使用时自动再拉起
    /// </summary>
    public void Stop() => _host.Restart();

    /// <summary>
    /// 直接换用一个版本，不落盘（测试与安装回调用）
    /// </summary>
    internal void UseVersion(AudioCppVersion? version)
    {
        // 先换再重启：反过来的话，中间进来的请求会用旧版本把进程拉起来
        SetCurrentVersion(version);
        _host.Restart();
    }

    /// <summary>
    /// 模型所属的家族；引擎没装或认不出为 null
    /// </summary>
    /// <param name="model">模型</param>
    public AudioCppFamily? GetFamily(AudioCppModelEntry model) =>
        GetFamilies().FirstOrDefault(x => x.Family == model.Family);

    // 会不会这种语言：spec 没列语种（或认不出家族）为 null，不当作「不会」
    private bool? Speaks(AudioCppModelEntry model, string language)
    {
        AudioCppFamily? family = GetFamily(model);
        if (family == null || family.Languages.Count == 0) return null;
        return family.Languages.Any(x => x.StartsWith(language, StringComparison.OrdinalIgnoreCase));
    }

    private void SetCurrentVersion(AudioCppVersion? version)
    {
        bool changed = CurrentVersion?.Name != version?.Name;
        lock (_gate)
        {
            if (changed) _families = null;
        }

        CurrentVersion = version;
        if (changed) VersionChanged?.Invoke();
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static AudioCppModelEntry? Find(IReadOnlyList<AudioCppModelEntry> models, string? selected, string task)
    {
        return models.FirstOrDefault(x => x.Task == task && x.Id == selected)
               ?? models.FirstOrDefault(x => x.Task == task);
    }

    private LocalServerLaunch CreateLaunch()
    {
        AudioCppVersion version = CurrentVersion ?? throw new LocalEngineNotReadyException();
        IReadOnlyList<AudioCppModelEntry> models = GetModels(true);
        lock (_gate) _launchedModels = models;

        string exe = Path.Combine(version.ExecutablePath,
            AudioCppVersionManager.ServerExeName + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (Voices != null) Directory.CreateDirectory(Voices.Directory);
        AudioCppServerOptions options = BuildServerOptions(version.Name, _settings, Voices?.Directory);
        _launchedBackend = options.Backend;
        return AudioCppServerConfig.Launch(exe, _configPath, models, options,
            TimeSpan.FromSeconds(Math.Max(10, _settings.ReadyTimeoutSeconds)));
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
}
