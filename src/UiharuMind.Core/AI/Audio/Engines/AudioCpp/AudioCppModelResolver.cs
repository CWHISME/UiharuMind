namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// audio.cpp 的模型面：本机装了哪些语音模型、某个任务（与语种）该用哪个、在用引擎自带的 spec 里有哪些家族。
/// 模型目录扫描与 spec 都缓存着；换引擎版本时清 spec 缓存（spec 随引擎包走）
/// </summary>
public sealed class AudioCppModelResolver
{
    private readonly AudioCppSettingConfig _settings;
    private readonly Func<AudioCppVersion?> _currentVersion;
    private readonly Func<IReadOnlyList<AudioCppModelEntry>> _scanModels;
    private readonly Lock _gate = new();
    private IReadOnlyList<AudioCppModelEntry>? _models; //扫描缓存
    private IReadOnlyList<AudioCppFamily>? _families; //在用引擎的 spec

    /// <param name="settings">引擎设置（选了哪个模型）</param>
    /// <param name="currentVersion">在用的引擎版本</param>
    /// <param name="scanModels">扫描本地音频模型</param>
    public AudioCppModelResolver(AudioCppSettingConfig settings, Func<AudioCppVersion?> currentVersion,
        Func<IReadOnlyList<AudioCppModelEntry>> scanModels)
    {
        _settings = settings;
        _currentVersion = currentVersion;
        _scanModels = scanModels;
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
    public AudioCppModelEntry Resolve(EAudioCppTask task)
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
    public AudioCppModelEntry Resolve(EAudioCppTask task, string? language)
    {
        AudioCppModelEntry preferred = Resolve(task);
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
        string? install = _currentVersion()?.InstallDirectory;
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
    /// 模型所属的家族；引擎没装或认不出为 null
    /// </summary>
    /// <param name="model">模型</param>
    public AudioCppFamily? GetFamily(AudioCppModelEntry model) =>
        GetFamilies().FirstOrDefault(x => x.Family == model.Family);

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
    /// 换了引擎版本：spec 跟着换，下次重读
    /// </summary>
    public void OnVersionChanged()
    {
        lock (_gate) _families = null;
    }

    // 会不会这种语言：spec 没列语种（或认不出家族）为 null，不当作「不会」
    private bool? Speaks(AudioCppModelEntry model, string language)
    {
        AudioCppFamily? family = GetFamily(model);
        if (family == null || family.Languages.Count == 0) return null;
        return family.Languages.Any(x => x.StartsWith(language, StringComparison.OrdinalIgnoreCase));
    }

    private static AudioCppModelEntry? Find(IReadOnlyList<AudioCppModelEntry> models, string? selected, string task)
    {
        return models.FirstOrDefault(x => x.Task == task && x.Id == selected)
               ?? models.FirstOrDefault(x => x.Task == task);
    }
}
