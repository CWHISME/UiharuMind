using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Voices;

/// <summary>
/// 音色从哪来
/// </summary>
public enum EVoiceSource
{
    /// <summary>让模型凭空念一句，不满意再抽</summary>
    Draw,

    /// <summary>用麦克风录自己的声音</summary>
    Record,

    /// <summary>导入一个音频文件</summary>
    Import
}

/// <summary>
/// 来源切换里的一项
/// </summary>
/// <param name="Source">来源</param>
/// <param name="Title">显示名</param>
public sealed record VoiceSourceOption(EVoiceSource Source, string Title);

/// <summary>
/// 抽到的一张
/// </summary>
public partial class VoiceDrawItem : ObservableObject
{
    [ObservableProperty] private bool _isPlaying;

    /// <param name="number">第几抽</param>
    /// <param name="model">用哪个模型抽的</param>
    /// <param name="draft">抽到的音色</param>
    public VoiceDrawItem(int number, VoiceDrawModel model, VoiceDraft draft)
    {
        Number = number;
        Model = model;
        Draft = draft;
        Label = Loc.Text(LangKey.VoiceDrawItem, number, VoiceCreateViewData.FormatDuration(draft.Wav));
    }

    public int Number { get; }

    public VoiceDrawModel Model { get; }

    public VoiceDraft Draft { get; }

    /// <summary>
    /// 形如「第 3 抽 · 7.5 秒」
    /// </summary>
    public string Label { get; }
}

/// <summary>
/// 添加音色：抽卡、录音或导入得到一段参考音频，配上它念的文字，起个名字存进音色库
/// </summary>
public partial class VoiceCreateViewData : ObservableObject
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RecommendedMaxDuration = TimeSpan.FromSeconds(20);

    private readonly VoiceCreateContext _context;
    private readonly VoiceClipPlayer _clips;
    private readonly Stopwatch _recordWatch = new();
    private readonly object _currentClipOwner = new(); //试听当前这段时的播放归属
    private CancellationTokenSource? _work; //抽卡、识别这些后台活，关窗时取消
    private byte[]? _clip;
    private string _autoName = ""; //随来源自动填的名字；用户改过就不再跟
    private int _drawCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDrawSource), nameof(IsRecordSource), nameof(IsImportSource),
        nameof(SelectedSourceOption), nameof(CanTranscribe))]
    private EVoiceSource _source;

    [ObservableProperty] private VoiceDrawModel? _selectedDrawModel;
    [ObservableProperty] private VoiceDrawItem? _selectedDraw;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isDrawing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isRecording;

    [ObservableProperty] private double _recordLevel;
    [ObservableProperty] private string _recordTimeText = "";
    [ObservableProperty] private string _importedFileName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClip), nameof(CanSave), nameof(CanTranscribe))]
    private string _clipInfo = "";

    [ObservableProperty] private string _clipWarning = "";
    [ObservableProperty] private bool _isPlayingClip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(CanTranscribe))]
    private bool _isTranscribing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _transcript = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(CanSave))]
    private string _name = "";

    /// <param name="context">要用到的服务</param>
    public VoiceCreateViewData(VoiceCreateContext context)
    {
        _context = context;
        _clips = new VoiceClipPlayer(context.Player);
        _clips.Changed += OnClipsChanged;
        SelectedDrawModel = context.DrawModels.FirstOrDefault();
        Source = CanDraw ? EVoiceSource.Draw : EVoiceSource.Record;
        RecordPrompt = VoiceReferenceText.For([],
            LocalizationManager.Instance.CurrentCulture.TwoLetterISOLanguageName);
    }

    /// <summary>
    /// 能抽卡的模型
    /// </summary>
    public IReadOnlyList<VoiceDrawModel> DrawModels => _context.DrawModels;

    /// <summary>
    /// 抽到的，新的在前
    /// </summary>
    public ObservableCollection<VoiceDrawItem> Draws { get; } = [];

    /// <summary>
    /// 本机有能抽卡的模型
    /// </summary>
    public bool CanDraw => _context.DrawModels.Count > 0;

    /// <summary>
    /// 来源切换：抽卡、录音、导入
    /// </summary>
    public IReadOnlyList<VoiceSourceOption> Sources { get; } =
    [
        new(EVoiceSource.Draw, Loc.Text(LangKey.VoiceSourceDraw)),
        new(EVoiceSource.Record, Loc.Text(LangKey.VoiceSourceRecord)),
        new(EVoiceSource.Import, Loc.Text(LangKey.VoiceSourceImport))
    ];

    public VoiceSourceOption SelectedSourceOption
    {
        get => Sources.First(x => x.Source == Source);
        set
        {
            if (value != null) Source = value.Source;
        }
    }

    public bool IsDrawSource => Source == EVoiceSource.Draw;

    public bool IsRecordSource => Source == EVoiceSource.Record;

    public bool IsImportSource => Source == EVoiceSource.Import;

    /// <summary>
    /// 录音时请用户念的句子
    /// </summary>
    public string RecordPrompt { get; }

    /// <summary>
    /// 有一段待存的参考音频
    /// </summary>
    public bool HasClip => ClipInfo.Length > 0;

    /// <summary>
    /// 能把参考音频识别成文字（本机有识别模型、不是抽卡来的）
    /// </summary>
    public bool CanTranscribe => _context.Transcribe != null && HasClip && !IsTranscribing && Source != EVoiceSource.Draw;

    public string NameError
    {
        get
        {
            string? name = VoiceLibrary.NormalizeName(Name);
            if (name == null) return Name.Length > 0 ? Loc.Text(LangKey.VoiceNameInvalid) : "";
            return _context.Library.Contains(name) ? Loc.Text(LangKey.VoiceNameTaken) : "";
        }
    }

    public bool HasNameError => NameError.Length > 0;

    public bool CanSave => HasClip && !IsDrawing && !IsRecording && !IsTranscribing &&
                           !string.IsNullOrWhiteSpace(Transcript) &&
                           VoiceLibrary.NormalizeName(Name) != null && !HasNameError;

    /// <summary>
    /// 秒数文案，认不出为空
    /// </summary>
    /// <param name="wav">WAV 字节</param>
    public static string FormatDuration(byte[] wav) =>
        WavEncoder.GetDuration(wav) is { } duration ? Loc.Text(LangKey.VoiceSeconds, duration.TotalSeconds.ToString("0.0")) : "";

    /// <summary>
    /// 导入一个音频文件（窗口选好文件后调）
    /// </summary>
    /// <param name="path">文件路径</param>
    public async Task ImportAsync(string path)
    {
        AudioRecording clip;
        try
        {
            clip = await Task.Run(() => _context.ReadFile(path));
        }
        catch (Exception e) when (e is NotSupportedException or IOException or UnauthorizedAccessException or AudioDeviceException)
        {
            _context.Messages.ShowNotification(Loc.Text(LangKey.VoiceImportFailed, e.Message), severity: MessageSeverity.Error);
            return;
        }

        ImportedFileName = Path.GetFileName(path);
        SetClip(clip.Wav, "", Path.GetFileNameWithoutExtension(path));
        if (clip.IsSilent) ClipWarning = Loc.Text(LangKey.VoiceClipSilent);
        await TranscribeAsync();
    }

    /// <summary>
    /// 存进音色库。抽卡来的顺带设成抽它的那个模型的音色
    /// </summary>
    /// <returns>存好的音色；还不能存或没存成为 null</returns>
    public VoiceSample? Save()
    {
        if (!CanSave || _clip == null || VoiceLibrary.NormalizeName(Name) is not { } name) return null;
        VoiceSample sample;
        try
        {
            sample = _context.Library.Save(name, _clip, Transcript.Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _context.Messages.ShowNotification(Loc.Text(LangKey.VoiceSaveFailed, e.Message), severity: MessageSeverity.Error);
            return null;
        }

        if (Source == EVoiceSource.Draw && SelectedDraw != null) _context.UseForModel(SelectedDraw.Model.Id, name);
        return sample;
    }

    /// <summary>
    /// 关窗：停录音、停试听、取消后台活
    /// </summary>
    public void Close()
    {
        _work?.Cancel();
        if (IsRecording) StopRecording(false);
        _clips.Stop();
    }

    partial void OnSourceChanged(EVoiceSource value)
    {
        if (IsRecording) StopRecording(false);
        _clips.Stop();
        if (value == EVoiceSource.Draw && SelectedDraw != null) SelectDraw(SelectedDraw);
        else ClearClip();
    }

    partial void OnSelectedDrawChanged(VoiceDrawItem? value)
    {
        if (value != null) SelectDraw(value);
    }

    partial void OnNameChanged(string value)
    {
        if (value != _autoName) _autoName = "";
    }

    // 抽一张：随机种子决定声音，新的排最前并选中、自动放一遍
    [RelayCommand]
    private async Task Draw()
    {
        if (SelectedDrawModel is not { } model || IsDrawing) return;
        IsDrawing = true;
        _work ??= new CancellationTokenSource();
        try
        {
            VoiceDraft draft = await _context.Draw(model.Id, Random.Shared.Next(1, int.MaxValue), _work.Token);
            VoiceDrawItem item = new(++_drawCount, model, draft);
            Draws.Insert(0, item);
            SelectedDraw = item;
            await PlayClip();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (AudioErrorText.IsExpected(e))
        {
            _context.Messages.ShowNotification(Loc.Text(LangKey.VoiceDrawFailed, AudioErrorText.Describe(e)),
                severity: MessageSeverity.Warning);
        }
        finally
        {
            IsDrawing = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PlayDraw(VoiceDrawItem item)
    {
        SelectedDraw = item;
        await Play(item, item.Draft.Wav);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PlayClip()
    {
        if (_clip == null) return;
        object owner = Source == EVoiceSource.Draw && SelectedDraw != null ? SelectedDraw : _currentClipOwner;
        await Play(owner, _clip);
    }

    // 再点一下停；停下后就是这段参考音频
    [RelayCommand]
    private void ToggleRecording()
    {
        if (IsRecording)
        {
            StopRecording(true);
            return;
        }

        _clips.Stop();
        ClearClip();
        try
        {
            _context.Recorder.LevelChanged += OnLevel;
            _context.Recorder.Start(VoiceCreateContext.SampleRate);
        }
        catch (Exception e) when (e is AudioDeviceException or InvalidOperationException)
        {
            _context.Recorder.LevelChanged -= OnLevel;
            _context.Messages.ShowNotification(e is InvalidOperationException
                ? Loc.Text(LangKey.VoiceRecorderBusy)
                : AudioErrorText.Describe(e), severity: MessageSeverity.Error);
            return;
        }

        _recordWatch.Restart();
        RecordTimeText = Loc.Text(LangKey.VoiceSeconds, "0.0");
        IsRecording = true;
    }

    [RelayCommand]
    private Task Transcribe() => TranscribeAsync();

    private async Task TranscribeAsync()
    {
        if (_context.Transcribe is not { } transcribe || _clip == null) return;
        IsTranscribing = true;
        _work ??= new CancellationTokenSource();
        try
        {
            string text = (await transcribe(_clip, _work.Token)).Trim();
            if (text.Length > 0) Transcript = text;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (AudioErrorText.IsExpected(e))
        {
            _context.Messages.ShowNotification(Loc.Text(LangKey.VoiceTranscribeFailed, AudioErrorText.Describe(e)),
                severity: MessageSeverity.Warning);
        }
        finally
        {
            IsTranscribing = false;
        }
    }

    private void StopRecording(bool keep)
    {
        _context.Recorder.LevelChanged -= OnLevel;
        IsRecording = false;
        RecordLevel = 0;
        _recordWatch.Stop();
        if (!keep)
        {
            _context.Recorder.Cancel();
            return;
        }

        AudioRecording? recording = _context.Recorder.Stop();
        if (recording == null) return;
        if (recording.IsSilent)
        {
            _context.Messages.ShowNotification(Loc.Text(LangKey.VoiceClipSilent), severity: MessageSeverity.Warning);
            return;
        }

        SetClip(recording.Wav, RecordPrompt, Loc.Text(LangKey.VoiceDefaultRecordName));
    }

    // 音频线程上来的：切回界面线程再改
    private void OnLevel(float level)
    {
        double seconds = _recordWatch.Elapsed.TotalSeconds;
        _context.Post(() =>
        {
            if (!IsRecording) return;
            RecordLevel = level;
            RecordTimeText = Loc.Text(LangKey.VoiceSeconds, seconds.ToString("0.0"));
        });
    }

    private async Task Play(object owner, byte[] wav)
    {
        try
        {
            await _clips.ToggleAsync(owner, wav);
        }
        catch (Exception e) when (AudioErrorText.IsExpected(e))
        {
            _context.Messages.ShowNotification(AudioErrorText.Describe(e), severity: MessageSeverity.Error);
        }
    }

    private void SelectDraw(VoiceDrawItem item) =>
        SetClip(item.Draft.Wav, item.Draft.Transcript, Loc.Text(LangKey.VoiceDefaultDrawName, item.Model.DisplayName, item.Number));

    private void SetClip(byte[] wav, string transcript, string suggestedName)
    {
        _clip = wav;
        Transcript = transcript;
        TimeSpan? duration = WavEncoder.GetDuration(wav);
        ClipWarning = duration < MinDuration ? Loc.Text(LangKey.VoiceClipTooShort)
            : duration > RecommendedMaxDuration ? Loc.Text(LangKey.VoiceClipTooLong)
            : "";
        ClipInfo = FormatDuration(wav) is { Length: > 0 } text ? text : "WAV";
        if (Name.Length == 0 || Name == _autoName)
        {
            _autoName = _context.Library.MakeUnique(VoiceLibrary.NormalizeName(suggestedName) ?? "voice");
            Name = _autoName;
        }
    }

    private void ClearClip()
    {
        _clip = null;
        ClipInfo = "";
        ClipWarning = "";
        Transcript = "";
    }

    private void OnClipsChanged()
    {
        IsPlayingClip = _clips.Owner != null && (ReferenceEquals(_clips.Owner, _currentClipOwner) ||
                                                 ReferenceEquals(_clips.Owner, SelectedDraw));
        foreach (VoiceDrawItem item in Draws) item.IsPlaying = ReferenceEquals(_clips.Owner, item);
    }
}
