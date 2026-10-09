using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// stable-diffusion.cpp 本地生图引擎，由分发（版本与安装，<see cref="Distribution"/>）与进程（<see cref="Runtime"/>）拼成。
/// sd-server 一个进程只装一个模型、一次只出一张：换模型就换进程，出图在这边排队；用完闲置一阵自动停，还回内存
/// </summary>
public sealed class SdCppEngine : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan }; //本机回环，超时由各次的取消控制
    private static readonly Lazy<SdCppEngine> SharedInstance = new(() => new SdCppEngine(
        SdCppSettingConfig.Current, Path.Combine(AppPaths.External.Engine, "SdCpp")));

    private readonly SdCppSettingConfig _settings;
    private readonly SdCppClient _client;
    private readonly LocalServerHost _host;
    private readonly SemaphoreSlim _generation = new(1, 1);
    private readonly Lock _gate = new();
    private SdCppModelSetup? _requested; //下次起进程装哪个
    private string? _launchedKey; //在跑的进程装的模型
    private string? _unloadedKey; //正要删文件的模型：为它起的那次不再起

    /// <param name="settings">引擎设置</param>
    /// <param name="engineRoot">引擎包目录</param>
    /// <param name="queue">下载队列；null 用全局的</param>
    /// <param name="http">本机回环用的客户端；null 用共享的</param>
    /// <param name="deviceInfo">取此刻的内存（拉起前检查）；null 用本机的</param>
    public SdCppEngine(SdCppSettingConfig settings, string engineRoot, DownloadQueue? queue = null,
        HttpClient? http = null, Func<RuntimeDeviceInfo>? deviceInfo = null)
    {
        _settings = settings;
        _client = new SdCppClient(http ?? Http);
        _host = new LocalServerHost(CreateLaunch,
            () => settings.IdleStopMinutes > 0 ? TimeSpan.FromMinutes(settings.IdleStopMinutes) : null,
            deviceInfo: deviceInfo);
        Distribution = new EngineDistribution<SdCppVersion>("stable-diffusion.cpp", engineRoot,
            new SdCppVersionManager(), queue ?? DownloadQueue.Shared, settings,
            isVetted: x => x.Version <= SdCppVersionManager.PinnedVersion);
        Distribution.VersionChanged += _host.Stop;
    }

    /// <summary>
    /// 走应用档案的那一个
    /// </summary>
    public static SdCppEngine Shared => SharedInstance.Value;

    /// <summary>
    /// 版本与安装
    /// </summary>
    public EngineDistribution<SdCppVersion> Distribution { get; }

    /// <summary>
    /// 进程在不在跑、停掉释放内存
    /// </summary>
    public IEngineRuntime Runtime => _host;

    /// <summary>
    /// 引擎设置
    /// </summary>
    public SdCppSettingConfig Settings => _settings;

    /// <summary>
    /// 停掉，还回内存；下次出图自动再起
    /// </summary>
    public void Stop() => _host.Stop();

    /// <summary>
    /// 要删某个模型的文件前调：进程装着它、或正要为它起，就停掉（放开文件）；装着别的模型不动
    /// </summary>
    /// <param name="model">模型</param>
    public void UnloadIfLoaded(SdCppModelSetup model)
    {
        bool loaded;
        lock (_gate)
        {
            loaded = _launchedKey == model.Key || _requested?.Key == model.Key;
            // 正在为它起的那次别再拿要删的文件重起，按缺组件失败（方言层归为这一家用不了）
            if (_requested?.Key == model.Key) _unloadedKey = model.Key;
        }

        if (loaded) _host.Stop();
    }

    /// <summary>
    /// 出一张图，此刻内存不够就不起（<see cref="EngineMemoryException"/>）
    /// </summary>
    public Task<IReadOnlyList<byte[]>> GenerateAsync(SdCppModelSetup model, SdCppImageRequest request,
        IProgress<ImageProgress>? progress, CancellationToken cancellationToken) =>
        GenerateAsync(model, request, progress, false, cancellationToken);

    /// <summary>
    /// 出一张图：没在跑就起（换了模型就换进程），排队、采样、取图。进度在后台线程报
    /// </summary>
    /// <param name="model">模型</param>
    /// <param name="request">请求</param>
    /// <param name="progress">进度；null 不报</param>
    /// <param name="allowMemoryRisk">此刻内存不够也照样起（问过用户的）</param>
    /// <param name="cancellationToken">取消：排队中的撤掉，在算的停进程（下次重新加载）</param>
    /// <returns>出好的图（PNG）</returns>
    /// <exception cref="SdCppModelMissingException">缺文件</exception>
    /// <exception cref="LocalEngineNotReadyException">没装引擎</exception>
    /// <exception cref="EngineMemoryException">此刻内存不够，没起</exception>
    /// <exception cref="LocalServerException">起不来</exception>
    /// <exception cref="SdCppRequestException">参数不合法</exception>
    /// <exception cref="SdCppGenerationException">任务失败</exception>
    /// <exception cref="TimeoutException">超时</exception>
    public async Task<IReadOnlyList<byte[]>> GenerateAsync(SdCppModelSetup model, SdCppImageRequest request,
        IProgress<ImageProgress>? progress, bool allowMemoryRisk, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> missing = model.MissingFiles();
        if (missing.Count > 0) throw new SdCppModelMissingException(model.Name, missing);

        progress?.Report(new ImageProgress(EImageStage.Queued));
        await _generation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 排队期间可能被删了
            missing = model.MissingFiles();
            if (missing.Count > 0) throw new SdCppModelMissingException(model.Name, missing);

            using IDisposable use = _host.BeginUse();
            Uri server = await GetServerAsync(model, progress, allowMemoryRisk, cancellationToken)
                .ConfigureAwait(false);
            string id = await _client.SubmitAsync(server, request, _settings.VaeTiling, cancellationToken)
                .ConfigureAwait(false);
            return await WaitForJobAsync(server, id, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _generation.Release();
        }
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private async Task<IReadOnlyList<byte[]>> WaitForJobAsync(Uri server, string id,
        IProgress<ImageProgress>? progress, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(Math.Max(30, _settings.GenerationTimeoutSeconds)));
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        string status = "queued";
        try
        {
            while (true)
            {
                SdCppJob job = await _client.GetJobAsync(server, id, linked.Token).ConfigureAwait(false);
                status = job.Status;
                switch (job.Status)
                {
                    case "completed":
                        return job.Images;
                    case "failed":
                        throw new SdCppGenerationException(job.ErrorCode, job.ErrorMessage);
                    case "cancelled":
                        throw new OperationCanceledException(job.ErrorMessage);
                    case "generating":
                        progress?.Report(new ImageProgress(EImageStage.Generating, job.Step, job.TotalSteps, job.Preview));
                        break;
                }

                await Task.Delay(PollInterval, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // 排队中的撤得掉；在算的撤不了，只能停进程。上次看到排队、这会儿可能已经开算，撤不掉也停
            if (status != "queued" || !await _client.TryCancelAsync(server, id).ConfigureAwait(false)) _host.Stop();
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException(
                    $"Image was not ready within {_settings.GenerationTimeoutSeconds}s; the engine was stopped.");
            throw;
        }
    }

    private async Task<Uri> GetServerAsync(SdCppModelSetup model, IProgress<ImageProgress>? progress,
        bool allowMemoryRisk, CancellationToken cancellationToken)
    {
        SdCppVersion version = await Distribution.EnsureCurrentAsync(cancellationToken).ConfigureAwait(false)
                               ?? throw new LocalEngineNotReadyException();
        lock (_gate)
        {
            _requested = model;
            _unloadedKey = null;
        }

        progress?.Report(new ImageProgress(EImageStage.Starting));
        void OnLoad() => progress?.Report(new ImageProgress(EImageStage.Starting) { LoadProgress = _host.LoadProgress });
        _host.LoadProgressChanged += OnLoad;
        try
        {
            return await _host.GetBaseUriAsync(Fingerprint(BuildLaunch(version, model)), allowMemoryRisk,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _host.LoadProgressChanged -= OnLoad;
        }
    }

    // 宿主在锁外调这里
    private LocalServerLaunch CreateLaunch()
    {
        SdCppVersion version = Distribution.CurrentVersion ?? throw new LocalEngineNotReadyException();
        SdCppModelSetup model;
        lock (_gate)
        {
            model = _requested ?? throw new InvalidOperationException("No image model was requested.");
            if (model.Key == _unloadedKey)
                throw new SdCppModelMissingException(model.Name, model.Components.Select(x => x.Path).ToList());
        }
        lock (_gate) _launchedKey = model.Key;
        return BuildLaunch(version, model) with { EstimatedMemoryBytes = SdCppMemory.Estimate(model) };
    }

    private LocalServerLaunch BuildLaunch(SdCppVersion version, SdCppModelSetup model) => SdCppServerArgs.Launch(
        version.GetExecutable(SdCppVersionManager.ExecutableName), SdCppServerArgs.Build(model, _settings),
        TimeSpan.FromSeconds(Math.Max(30, _settings.LoadTimeoutSeconds)));

    // 只看程序与参数：加载超时不是进程参数，改它不必重新加载权重
    private static string Fingerprint(LocalServerLaunch launch) =>
        string.Join('\n', [launch.ExecutablePath, ..launch.BuildArguments(0)]);
}
