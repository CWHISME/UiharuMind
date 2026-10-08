namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 朗读、听写各用哪个：本地引擎（具体哪个模型由引擎设置管）或某个在线服务。
/// 「用哪个」只存 id（<see cref="AudioSettingConfig"/>）；服务被删了、改成了别的任务，就回到本地引擎。
/// 本地引擎没有这个任务的模型时，退到第一个能用的在线服务（只配了 Edge TTS 也能直接朗读）
/// </summary>
public sealed class AudioRouter : IDisposable
{
    private static readonly Lazy<AudioRouter> SharedInstance = new(() => new AudioRouter(() => AudioCppProvider.Shared,
        AudioSettingConfig.Current, AudioServiceSettingConfig.Current,
        hasLocal: (task, language) => AudioCppEngine.Shared.HasModelFor(
            task == EAudioCapability.Tts ? EAudioCppTask.Tts : EAudioCppTask.Asr, language)));

    private readonly Func<IAudioProvider> _local;
    private readonly AudioSettingConfig _settings;
    private readonly AudioServiceSettingConfig _services;
    private readonly Func<AudioServiceInfo, IAudioProvider> _create;
    private readonly Func<EAudioCapability, string?, bool> _hasLocal;
    private readonly Lock _gate = new();
    private readonly Dictionary<AudioServiceInfo, IAudioProvider> _providers = new(ReferenceEqualityComparer.Instance);

    /// <param name="local">本地引擎的 provider（第一次用才建）</param>
    /// <param name="settings">「用哪个」</param>
    /// <param name="services">在线服务列表</param>
    /// <param name="create">按条目建 provider；null 走 <see cref="AudioServiceProviderFactory"/></param>
    /// <param name="hasLocal">本地引擎有没有这个任务的模型（给了语种还要会这种语言）；null 当作有</param>
    public AudioRouter(Func<IAudioProvider> local, AudioSettingConfig settings, AudioServiceSettingConfig services,
        Func<AudioServiceInfo, IAudioProvider>? create = null, Func<EAudioCapability, string?, bool>? hasLocal = null)
    {
        _hasLocal = hasLocal ?? ((_, _) => true);
        _local = local;
        _settings = settings;
        _services = services;
        _create = create ?? AudioServiceProviderFactory.Create;
        services.ServicesChanged += DropStaleProviders;
    }

    /// <summary>
    /// 走应用档案的那一个（界面与智能体工具共用；第一次用才建）
    /// </summary>
    public static AudioRouter Shared => SharedInstance.Value;

    /// <summary>
    /// 默认换了
    /// </summary>
    public event Action? DefaultsChanged;

    /// <summary>
    /// 在线服务（某个任务的）
    /// </summary>
    /// <param name="task">任务；null 全部</param>
    public IReadOnlyList<AudioServiceInfo> Services(EAudioCapability? task = null) =>
        _services.Services.Where(x => task == null || x.Task == task).ToList();

    /// <summary>
    /// 这个任务实际默认用的在线服务（选了的，或本地没模型时退到的那个）；用本地引擎为 null
    /// </summary>
    /// <param name="task">合成 / 识别</param>
    public AudioServiceInfo? DefaultService(EAudioCapability task) =>
        FindService(PreferredId(task), task) ??
        (_hasLocal(task, null) ? null : _services.Services.FirstOrDefault(x => x.Task == task && x.IsConfigured));

    /// <summary>
    /// 拿 provider
    /// </summary>
    /// <param name="task">合成 / 识别</param>
    /// <param name="providerId">指定哪个（如试听某个在线服务）；null 用默认</param>
    /// <returns>provider；在线服务找不到时是本地引擎</returns>
    public IAudioProvider Resolve(EAudioCapability task, string? providerId = null)
    {
        AudioServiceInfo? service = providerId != null ? FindService(providerId, task) : DefaultService(task);
        if (service == null) return _local();
        lock (_gate)
        {
            if (!_providers.TryGetValue(service, out IAudioProvider? provider))
                _providers[service] = provider = _create(service);
            return provider;
        }
    }

    /// <summary>
    /// 在线服务这次没成时退到哪：用户开了「改用本地」、本地引擎有会这种语言的模型，就是它
    /// </summary>
    /// <param name="task">合成 / 识别</param>
    /// <param name="failed">失败的那个</param>
    /// <param name="language">文字语种；null 不挑</param>
    /// <returns>本地引擎；没开、失败的本来就是本地、或本地没合适的模型为 null</returns>
    public IAudioProvider? Fallback(EAudioCapability task, IAudioProvider failed, string? language = null) =>
        !_settings.FallbackToLocal || failed.IsLocal || !_hasLocal(task, language) ? null : _local();

    /// <summary>
    /// 默认改回本地引擎
    /// </summary>
    /// <param name="task">合成 / 识别</param>
    public void UseLocal(EAudioCapability task) => SetPreferred(task, null);

    /// <summary>
    /// 默认改用某个在线服务
    /// </summary>
    /// <param name="service">服务</param>
    public void UseService(AudioServiceInfo service) => SetPreferred(service.Task, service.Id);

    /// <summary>
    /// 服务改了名：默认指着旧名的跟过去
    /// </summary>
    /// <param name="oldName">旧名</param>
    /// <param name="service">改好的服务</param>
    public void OnServiceRenamed(string oldName, AudioServiceInfo service)
    {
        string oldId = AudioServiceInfo.IdPrefix + oldName;
        foreach (EAudioCapability task in new[] { EAudioCapability.Tts, EAudioCapability.Asr })
            if (PreferredId(task) == oldId)
                SetPreferred(task, service.Task == task ? service.Id : null);
    }

    public void Dispose()
    {
        _services.ServicesChanged -= DropStaleProviders;
        lock (_gate)
        {
            foreach (IAudioProvider provider in _providers.Values) (provider as IDisposable)?.Dispose();
            _providers.Clear();
        }
    }

    private string? PreferredId(EAudioCapability task) =>
        task == EAudioCapability.Tts ? _settings.PreferredTtsProviderId : _settings.PreferredAsrProviderId;

    private AudioServiceInfo? FindService(string? id, EAudioCapability task) =>
        id?.StartsWith(AudioServiceInfo.IdPrefix, StringComparison.Ordinal) == true
            ? _services.Services.FirstOrDefault(x => x.Id == id && x.Task == task && x.IsConfigured)
            : null;

    private void SetPreferred(EAudioCapability task, string? id)
    {
        if (PreferredId(task) == id) return;
        if (task == EAudioCapability.Tts) _settings.PreferredTtsProviderId = id;
        else _settings.PreferredAsrProviderId = id;
        _settings.Save();
        DefaultsChanged?.Invoke();
    }

    // 列表整份换过：不在新列表里的条目（含改过的旧实例）对应的 provider 丢掉。在途请求自己跑完，不在这里释放
    private void DropStaleProviders()
    {
        lock (_gate)
        {
            HashSet<AudioServiceInfo> live = new(_services.Services, ReferenceEqualityComparer.Instance);
            foreach (AudioServiceInfo stale in _providers.Keys.Where(x => !live.Contains(x)).ToList())
                _providers.Remove(stale);
        }
    }
}
