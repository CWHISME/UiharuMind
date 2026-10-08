using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Models.Shared;

namespace UiharuMind.Features.Characters.Voice;

/// <summary>
/// 角色编辑「高级」里的朗读声音：用哪个、音色、语速、语气描述，可当场试听。每改一项就写回草稿，随角色一起保存
/// </summary>
public partial class CharacterVoiceViewData : ObservableObject, IDisposable
{
    private readonly Action<SpeechStyle?> _commit;
    private readonly Func<CharacterVoiceSource, CancellationToken, Task<IReadOnlyList<string>>> _listVoices;
    private readonly IReadAloudService _readAloud;
    private readonly Func<string> _sampleText;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loading; //回填初值期间不写回

    [ObservableProperty] private CharacterVoiceSource _selectedSource;
    [ObservableProperty] private bool _hasOwnSpeed;
    [ObservableProperty] private double _speed;
    [ObservableProperty] private string _instruction;
    [ObservableProperty] private bool _isLoadingVoices;
    [ObservableProperty] private bool _isPreviewing;

    /// <param name="style">角色卡上存的声音；null 为没设</param>
    /// <param name="commit">写回草稿（什么都没设时写 null）</param>
    /// <param name="sources">能选的「用哪个」，「跟默认」在最前</param>
    /// <param name="listVoices">列这一项的全部音色（本地的会拉起引擎）</param>
    /// <param name="readAloud">试听（与别处的朗读、试听互相打断）</param>
    /// <param name="sampleText">试听念什么（如角色的开场白）</param>
    /// <param name="globalSpeed">全局语速（没单独设时显示它）</param>
    public CharacterVoiceViewData(SpeechStyle? style, Action<SpeechStyle?> commit,
        IReadOnlyList<CharacterVoiceSource> sources,
        Func<CharacterVoiceSource, CancellationToken, Task<IReadOnlyList<string>>> listVoices,
        IReadAloudService readAloud, Func<string> sampleText, float globalSpeed)
    {
        _commit = commit;
        _listVoices = listVoices;
        _readAloud = readAloud;
        _sampleText = sampleText;
        _loading = true;
        CharacterVoiceSource? current = sources.FirstOrDefault(x => x.Matches(style));
        Sources = current != null || style?.Provider == null ? sources : [.. sources, CharacterVoiceSource.Missing(style)];
        _selectedSource = current ?? Sources[^1];
        Voice.SetOptions(_selectedSource.KnownVoices);
        Voice.Text = style?.Voice ?? "";
        _hasOwnSpeed = style?.Speed != null;
        _speed = Math.Clamp(style?.Speed ?? globalSpeed, AudioSettingConfig.MinSpeed, AudioSettingConfig.MaxSpeed);
        _instruction = style?.Instruction ?? "";
        _loading = false;
        Voice.Changed += _ => Commit();
        readAloud.StateChanged += OnReadAloudChanged;
        if (!_selectedSource.IsLocal && !_selectedSource.IsDefault && _selectedSource.KnownVoices.Count == 0)
            _ = LoadVoicesAsync();
    }

    /// <summary>
    /// 能选的「用哪个」
    /// </summary>
    public IReadOnlyList<CharacterVoiceSource> Sources { get; }

    /// <summary>
    /// 音色：候选可选也能手输；空为模型默认
    /// </summary>
    public EditableChoice Voice { get; } = new();

    /// <summary>
    /// 跟默认时音色由默认的那个定，不单独选
    /// </summary>
    public bool CanChooseVoice => !SelectedSource.IsDefault;

    /// <summary>
    /// 本地模型的自带音色要拉起引擎才列得全，交给用户点
    /// </summary>
    public bool CanListVoices => SelectedSource.IsLocal;

    public double MinSpeed => AudioSettingConfig.MinSpeed;

    public double MaxSpeed => AudioSettingConfig.MaxSpeed;

    /// <summary>
    /// 形如「1.2×」
    /// </summary>
    public string SpeedText => $"{Speed:0.0#}×";

    /// <summary>
    /// 写了语气描述，但选的那个不听：提示一句会被忽略
    /// </summary>
    public bool IsInstructionIgnored => !string.IsNullOrWhiteSpace(Instruction) && !SelectedSource.AcceptsInstruction;

    /// <summary>
    /// 现在表单上的样子
    /// </summary>
    /// <returns>声音；什么都没设为 null</returns>
    public SpeechStyle? Build()
    {
        SpeechStyle style = new()
        {
            Provider = SelectedSource.Provider,
            Model = SelectedSource.Model,
            Voice = SelectedSource.IsDefault || string.IsNullOrWhiteSpace(Voice.Text) ? null : Voice.Text.Trim(),
            Speed = HasOwnSpeed ? (float)Math.Round(Speed, 2) : null,
            Instruction = string.IsNullOrWhiteSpace(Instruction) ? null : Instruction.Trim()
        };
        return style.IsEmpty ? null : style;
    }

    public void Dispose()
    {
        _readAloud.StateChanged -= OnReadAloudChanged;
        if (ReferenceEquals(_readAloud.Owner, this)) _readAloud.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    partial void OnSelectedSourceChanged(CharacterVoiceSource value)
    {
        Voice.SetOptions(value.KnownVoices);
        Voice.Text = ""; //上一个的音色这一个多半不认
        OnPropertyChanged(nameof(CanChooseVoice));
        OnPropertyChanged(nameof(CanListVoices));
        OnPropertyChanged(nameof(IsInstructionIgnored));
        StopPreview();
        Commit();
        if (!value.IsLocal && !value.IsDefault && value.KnownVoices.Count == 0) _ = LoadVoicesAsync();
    }

    partial void OnHasOwnSpeedChanged(bool value) => Commit();

    partial void OnSpeedChanged(double value)
    {
        OnPropertyChanged(nameof(SpeedText));
        Commit();
    }

    partial void OnInstructionChanged(string value)
    {
        OnPropertyChanged(nameof(IsInstructionIgnored));
        Commit();
    }

    // 已知的在前，列出来的补在后面
    [RelayCommand]
    private async Task LoadVoicesAsync()
    {
        CharacterVoiceSource source = SelectedSource;
        IsLoadingVoices = true;
        try
        {
            IReadOnlyList<string> voices = await _listVoices(source, _lifetime.Token);
            if (ReferenceEquals(source, SelectedSource)) Voice.SetOptions(source.KnownVoices.Concat(voices).Distinct());
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Debug($"List voices for character failed: {e.Message}");
        }
        finally
        {
            IsLoadingVoices = false;
        }
    }

    // 再点一下停；念的就是表单现在的样子
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Preview() => _readAloud.ToggleAsync(this, _sampleText(), Build(), preview: true);

    private void StopPreview()
    {
        if (ReferenceEquals(_readAloud.Owner, this)) _readAloud.Stop();
    }

    private void Commit()
    {
        if (!_loading) _commit(Build());
    }

    private void OnReadAloudChanged() =>
        IsPreviewing = ReferenceEquals(_readAloud.Owner, this) && _readAloud.State != EReadAloudState.Idle;
}
