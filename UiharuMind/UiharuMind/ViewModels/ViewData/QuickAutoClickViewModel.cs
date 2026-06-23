using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpHook.Data;
using UiharuMind.Core.AutoClick;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Services;

namespace UiharuMind.ViewModels.ViewData;

public partial class AutoClickStepViewModel : ObservableObject
{
    private readonly Action<AutoClickStepViewModel>? _onChanged;
    private bool _suspendChangeNotification;

    public AutoClickStepViewModel(AutoClickStepKind kind, Action<AutoClickStepViewModel>? onChanged)
    {
        _onChanged = onChanged;
        _kind = kind;
        Children.CollectionChanged += OnChildrenCollectionChanged;
        RefreshLocalized();
    }

    public static AutoClickStepViewModel CreateInitialized(
        AutoClickStepKind kind,
        Action<AutoClickStepViewModel>? onChanged,
        Action<AutoClickStepViewModel> initialize)
    {
        var step = new AutoClickStepViewModel(kind, onChanged)
        {
            _suspendChangeNotification = true
        };
        initialize(step);
        step._suspendChangeNotification = false;
        step.RefreshLocalized();
        return step;
    }

    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private AutoClickStepKind _kind;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private int _delay;
    [ObservableProperty] private MouseButton? _mouseButton;
    [ObservableProperty] private short _x;
    [ObservableProperty] private short _y;
    [ObservableProperty] private KeyCode? _keyCode;
    [ObservableProperty] private string? _text;
    [ObservableProperty] private int? _wheelDelta;
    [ObservableProperty] private int? _duration;
    [ObservableProperty] private int _loopCount = 1;
    [ObservableProperty] private int _level;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _kindDisplayName = string.Empty;

    public AutoClickStepViewModel? Parent { get; set; }
    public ObservableCollection<AutoClickStepViewModel> Children { get; } = new();

    public bool IsLoop => Kind == AutoClickStepKind.Loop;
    public bool HasChildren => Children.Count > 0;
    public string DelayText => string.Format(LocalizationManager.Instance.GetString("AutoClickDelayFormat"), Delay);

    public string DurationText => Duration.HasValue
        ? string.Format(LocalizationManager.Instance.GetString("AutoClickDurationFormat"), Duration.Value)
        : LocalizationManager.Instance.GetString("AutoClickNoDuration");

    partial void OnKindChanged(AutoClickStepKind value)
    {
        OnPropertyChanged(nameof(IsLoop));
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnIsEnabledChanged(bool value) => NotifyChanged();

    partial void OnDelayChanged(int value)
    {
        OnPropertyChanged(nameof(DelayText));
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnMouseButtonChanged(MouseButton? value)
    {
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnXChanged(short value)
    {
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnYChanged(short value)
    {
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnKeyCodeChanged(KeyCode? value)
    {
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnTextChanged(string? value)
    {
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnWheelDeltaChanged(int? value)
    {
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnDurationChanged(int? value)
    {
        OnPropertyChanged(nameof(DurationText));
        RefreshLocalized();
        NotifyChanged();
    }

    partial void OnLoopCountChanged(int value)
    {
        RefreshLocalized();
        NotifyChanged();
    }

    public void RefreshLocalized()
    {
        KindDisplayName = LocalizationManager.Instance.GetString(GetKindKey(Kind));
        Title = BuildTitle();
        Summary = BuildSummary();
        OnPropertyChanged(nameof(DelayText));
        OnPropertyChanged(nameof(DurationText));
    }

    public AutoClickStepData ToData()
    {
        return new AutoClickStepData
        {
            Id = Id,
            Kind = Kind,
            IsEnabled = IsEnabled,
            Delay = Delay,
            MouseButton = MouseButton,
            X = X,
            Y = Y,
            KeyCode = KeyCode,
            Text = Text,
            WheelDelta = WheelDelta,
            Duration = Duration,
            LoopCount = LoopCount,
            Children = Children.Select(x => x.ToData()).ToList()
        };
    }

    public static AutoClickStepViewModel FromData(AutoClickStepData data, Action<AutoClickStepViewModel>? onChanged)
    {
        var step = new AutoClickStepViewModel(data.Kind, onChanged)
        {
            Id = string.IsNullOrWhiteSpace(data.Id) ? Guid.NewGuid().ToString("N") : data.Id,
            IsEnabled = data.IsEnabled,
            Delay = data.Delay,
            MouseButton = data.MouseButton,
            X = data.X,
            Y = data.Y,
            KeyCode = data.KeyCode,
            Text = data.Text,
            WheelDelta = data.WheelDelta,
            Duration = data.Duration,
            LoopCount = Math.Max(1, data.LoopCount)
        };

        foreach (var childData in data.Children)
        {
            var child = FromData(childData, onChanged);
            child.Parent = step;
            step.Children.Add(child);
        }

        step.RefreshLocalized();
        return step;
    }

    public AutoClickStepViewModel Clone(Action<AutoClickStepViewModel>? onChanged)
    {
        var data = ToData();
        data.Id = Guid.NewGuid().ToString("N");
        ResetIds(data.Children);
        return FromData(data, onChanged);
    }

    private static void ResetIds(IEnumerable<AutoClickStepData> steps)
    {
        foreach (var step in steps)
        {
            step.Id = Guid.NewGuid().ToString("N");
            ResetIds(step.Children);
        }
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasChildren));
        RefreshLocalized();
        NotifyChanged();
    }

    private string BuildTitle()
    {
        var loc = LocalizationManager.Instance;
        return Kind switch
        {
            AutoClickStepKind.MouseClick => string.Format(loc.GetString("AutoClickStepMouseClickTitle"), GetMouseButtonName()),
            AutoClickStepKind.MouseDown => string.Format(loc.GetString("AutoClickStepMouseDownTitle"), GetMouseButtonName()),
            AutoClickStepKind.MouseUp => string.Format(loc.GetString("AutoClickStepMouseUpTitle"), GetMouseButtonName()),
            AutoClickStepKind.MouseMove => string.Format(loc.GetString("AutoClickStepMouseMoveTitle"), X, Y),
            AutoClickStepKind.MouseWheel => string.Format(loc.GetString("AutoClickStepMouseWheelTitle"), GetWheelDirectionName()),
            AutoClickStepKind.KeyClick => string.Format(loc.GetString("AutoClickStepKeyClickTitle"), GetKeyName()),
            AutoClickStepKind.KeyDown => string.Format(loc.GetString("AutoClickStepKeyDownTitle"), GetKeyName()),
            AutoClickStepKind.KeyUp => string.Format(loc.GetString("AutoClickStepKeyUpTitle"), GetKeyName()),
            AutoClickStepKind.Text => string.Format(loc.GetString("AutoClickStepTextTitle"), string.IsNullOrEmpty(Text) ? loc.GetString("AutoClickEmptyText") : Text),
            AutoClickStepKind.Delay => string.Format(loc.GetString("AutoClickStepDelayTitle"), Delay),
            AutoClickStepKind.Loop => string.Format(loc.GetString("AutoClickStepLoopTitle"), LoopCount),
            _ => Kind.ToString()
        };
    }

    private string BuildSummary()
    {
        var loc = LocalizationManager.Instance;
        return Kind switch
        {
            AutoClickStepKind.MouseClick or AutoClickStepKind.MouseDown or AutoClickStepKind.MouseUp =>
                string.Format(loc.GetString("AutoClickStepMouseSummary"), X, Y, Duration ?? 0),
            AutoClickStepKind.MouseMove => string.Format(loc.GetString("AutoClickStepPositionSummary"), X, Y),
            AutoClickStepKind.MouseWheel => string.Format(loc.GetString("AutoClickStepWheelSummary"), WheelDelta ?? 0),
            AutoClickStepKind.KeyClick or AutoClickStepKind.KeyDown or AutoClickStepKind.KeyUp =>
                string.Format(loc.GetString("AutoClickStepKeySummary"), GetKeyName(), Duration ?? 0),
            AutoClickStepKind.Text => Text ?? string.Empty,
            AutoClickStepKind.Delay => string.Format(loc.GetString("AutoClickDelayFormat"), Delay),
            AutoClickStepKind.Loop => string.Format(loc.GetString("AutoClickStepLoopSummary"), Children.Count),
            _ => string.Empty
        };
    }

    private string GetKeyName()
    {
        return KeyCode?.ToString().Replace("Vc", "") ?? LocalizationManager.Instance.GetString("AutoClickNoKey");
    }

    private string GetMouseButtonName()
    {
        var loc = LocalizationManager.Instance;
        return MouseButton switch
        {
            SharpHook.Data.MouseButton.Button1 => loc.GetString("AutoClickMouseLeft"),
            SharpHook.Data.MouseButton.Button2 => loc.GetString("AutoClickMouseRight"),
            SharpHook.Data.MouseButton.Button3 => loc.GetString("AutoClickMouseMiddle"),
            _ => loc.GetString("AutoClickMouseButton")
        };
    }

    private string GetWheelDirectionName()
    {
        var loc = LocalizationManager.Instance;
        return WheelDelta >= 0 ? loc.GetString("AutoClickWheelUp") : loc.GetString("AutoClickWheelDown");
    }

    private static string GetKindKey(AutoClickStepKind kind)
    {
        return kind switch
        {
            AutoClickStepKind.MouseClick => "AutoClickKindMouseClick",
            AutoClickStepKind.MouseDown => "AutoClickKindMouseDown",
            AutoClickStepKind.MouseUp => "AutoClickKindMouseUp",
            AutoClickStepKind.MouseMove => "AutoClickKindMouseMove",
            AutoClickStepKind.MouseWheel => "AutoClickKindMouseWheel",
            AutoClickStepKind.KeyClick => "AutoClickKindKeyClick",
            AutoClickStepKind.KeyDown => "AutoClickKindKeyDown",
            AutoClickStepKind.KeyUp => "AutoClickKindKeyUp",
            AutoClickStepKind.Text => "AutoClickKindText",
            AutoClickStepKind.Delay => "AutoClickKindDelay",
            AutoClickStepKind.Loop => "AutoClickKindLoop",
            _ => kind.ToString()
        };
    }

    private void NotifyChanged()
    {
        if (_suspendChangeNotification) return;
        _onChanged?.Invoke(this);
    }
}

public partial class QuickAutoClickViewModel : ViewModelBase
{
    private const int DefaultMouseMovementFrameRate = 30;
    private const int MinMouseMovementFrameRate = 1;
    private const int MaxMouseMovementFrameRate = 120;
    private const int MouseMoveSettleDelayMs = 35;
    private const int MouseMoveRecordMinDistance = 12;
    private const int PlaybackStartSettleDelayMs = 250;
    private const int KeyClickMergeThresholdMs = 350;
    private const int MouseClickMergeThresholdMs = 350;

    private CancellationTokenSource? _playbackCts;
    private DateTime _lastActionTime;
    private Action? _onStartRecording;
    private Action? _onStopRecording;
    private Func<CancellationToken, Task>? _onPreparePlayback;
    private Action<int, int>? _onPlaybackProgress;
    private Action? _onPlaybackFinished;
    private readonly Dictionary<KeyCode, DateTime> _keyPressTimes = new();
    private readonly Stopwatch _recordStopwatch = new();
    private IDisposable? _shortcutSuspendScope;
    private (short x, short y) _lastMousePosition;
    private (short x, short y) _mousePressPosition;
    private DateTime _lastMouseMoveTime;
    private DateTime _mousePressTime = DateTime.MinValue;
    private AutoClickStepViewModel? _pendingMouseDownStep;
    private bool _isMousePressed;
    private bool _hasRecordedMouseMovementDuringPress;
    private bool _isLoadingSession;
    private bool _isInternalUpdate;
    private string _statusKey = "AutoClickStatusReady";
    private object[] _statusArgs = [];
    private readonly List<AutoClickSession> _allSessions = new();

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private int _repeatCount = 1;
    [ObservableProperty] private int _defaultDelay = 100;
    [ObservableProperty] private double _playbackSpeed = 1.0;
    [ObservableProperty] private bool _recordMouseMovement;
    [ObservableProperty] private int _mouseMovementFrameRate = DefaultMouseMovementFrameRate;
    [ObservableProperty] private bool _recordMouseMovementOnlyWhenPressed = true;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _recordedActionsCount;
    [ObservableProperty] private AutoClickSession? _currentSession;
    [ObservableProperty] private AutoClickStepViewModel? _selectedStep;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<AutoClickStepViewModel> Steps { get; } = new();
    public ObservableCollection<AutoClickStepViewModel> VisibleSteps { get; private set; } = new();
    public ObservableCollection<AutoClickSession> SavedSessions { get; } = new();

    public IReadOnlyList<MouseButtonOption> MouseButtonOptions { get; } =
    [
        new(SharpHook.Data.MouseButton.Button1, "AutoClickMouseLeft"),
        new(SharpHook.Data.MouseButton.Button2, "AutoClickMouseRight"),
        new(SharpHook.Data.MouseButton.Button3, "AutoClickMouseMiddle")
    ];

    public int StepCount => CountSteps(Steps);
    public bool HasNoSteps => StepCount == 0;
    public bool HasSteps => StepCount > 0;
    public bool HasUnsavedChanges => IsDirty;
    public string CurrentSessionName => CurrentSession?.Name ?? LocalizationManager.Instance.GetString("AutoClickUntitledSession");
    public string CurrentSessionDisplayName => IsDirty ? $"{CurrentSessionName} *" : CurrentSessionName;
    public string UnsavedMarker => IsDirty ? "*" : string.Empty;
    public string WindowTitle => string.Format(LocalizationManager.Instance.GetString("AutoClickWindowTitleFormat"), CurrentSessionDisplayName);
    public string StepCountText => string.Format(LocalizationManager.Instance.GetString("AutoClickStepCountFormat"), StepCount);
    public string RecordedActionsText => string.Format(LocalizationManager.Instance.GetString("AutoClickRecordedCountFormat"), RecordedActionsCount);
    public bool HasSelectedStep => SelectedStep != null;
    public bool SelectedStepIsLoop => SelectedStep?.IsLoop == true;
    public bool SelectedStepIsMouse => SelectedStep?.Kind is AutoClickStepKind.MouseClick or AutoClickStepKind.MouseDown or AutoClickStepKind.MouseUp or AutoClickStepKind.MouseMove;
    public bool SelectedStepIsMouseButton => SelectedStep?.Kind is AutoClickStepKind.MouseClick or AutoClickStepKind.MouseDown or AutoClickStepKind.MouseUp;
    public bool SelectedStepIsWheel => SelectedStep?.Kind == AutoClickStepKind.MouseWheel;
    public bool SelectedStepIsKeyboard => SelectedStep?.Kind is AutoClickStepKind.KeyClick or AutoClickStepKind.KeyDown or AutoClickStepKind.KeyUp;
    public bool SelectedStepIsText => SelectedStep?.Kind == AutoClickStepKind.Text;
    public bool SelectedStepHasDuration => SelectedStep?.Kind is AutoClickStepKind.MouseClick or AutoClickStepKind.MouseDown or AutoClickStepKind.KeyClick or AutoClickStepKind.KeyDown;

    public event Action<int>? OnActionRecorded;

    public QuickAutoClickViewModel()
    {
        StatusText = LocalizationManager.Instance.GetString(_statusKey);
        Steps.CollectionChanged += OnRootStepsCollectionChanged;
        AutoClickManager.Instance.OnItemAdded += OnSessionAdded;
        AutoClickManager.Instance.OnItemRemoved += OnSessionRemoved;
        LocalizationManager.Instance.LanguageChanged += RefreshLocalized;
        LoadSavedSessions();
    }

    public void SetRecordingCallbacks(Action? onStart, Action? onStop)
    {
        _onStartRecording = onStart;
        _onStopRecording = onStop;
    }

    public void SetPlaybackCallback(
        Func<CancellationToken, Task>? onPrepare,
        Action<int, int>? onProgress,
        Action? onFinished = null)
    {
        _onPreparePlayback = onPrepare;
        _onPlaybackProgress = onProgress;
        _onPlaybackFinished = onFinished;
    }

    public void LoadSavedSessions()
    {
        _allSessions.Clear();
        _allSessions.AddRange(AutoClickManager.Instance.GetOrderedItems());
        RefreshFilteredSessions();
    }

    public async Task<bool> TryConfirmDiscardAsync(Func<string, Task<bool>> confirm)
    {
        if (!HasUnsavedChanges) return true;
        return await confirm(LocalizationManager.Instance.GetString("AutoClickDiscardUnsavedConfirm"));
    }

    public void LoadSession(AutoClickSession? session)
    {
        if (session == null) return;
        if (IsPlaying) StopPlayback();
        if (IsRecording) StopRecording();

        _isLoadingSession = true;
        _isInternalUpdate = true;
        CurrentSession = session;
        RepeatCount = session.RepeatCount;
        PlaybackSpeed = session.PlaybackSpeed;
        DefaultDelay = session.DefaultDelay;
        RecordMouseMovement = session.RecordMouseMovement;
        MouseMovementFrameRate = GetSessionMouseMovementFrameRate(session);
        RecordMouseMovementOnlyWhenPressed = GetSessionRecordMouseMovementOnlyWhenPressed(session);
        Steps.Clear();

        foreach (var stepData in session.Steps)
        {
            var step = AutoClickStepViewModel.FromData(stepData, OnStepChanged);
            step.Parent = null;
            Steps.Add(step);
        }

        _isInternalUpdate = false;
        RebuildVisibleSteps();
        SelectedStep = VisibleSteps.FirstOrDefault();
        IsDirty = false;
        _isLoadingSession = false;
        SetStatus("AutoClickStatusLoaded", session.Name, StepCount);
        NotifySessionProperties();
    }

    [RelayCommand]
    public void SaveSession()
    {
        if (CurrentSession == null)
        {
            CurrentSession = AutoClickManager.Instance.CreateNewSession(CreateDefaultSessionName("AutoClickDefaultSessionName"));
        }

        CurrentSession.RepeatCount = Math.Max(0, RepeatCount);
        CurrentSession.PlaybackSpeed = Math.Clamp(PlaybackSpeed, 0.1, 5.0);
        CurrentSession.DefaultDelay = Math.Max(0, DefaultDelay);
        CurrentSession.RecordMouseMovement = RecordMouseMovement;
        CurrentSession.MouseMovementFrameRate = NormalizeMouseMovementFrameRate(MouseMovementFrameRate);
        CurrentSession.RecordMouseMovementOnlyWhenPressed = RecordMouseMovementOnlyWhenPressed;
        CurrentSession.Steps = Steps.Select(x => x.ToData()).ToList();
        AutoClickManager.Instance.Save(CurrentSession);
        RefreshSessionListItem(CurrentSession);
        IsDirty = false;
        SetStatus("AutoClickStatusSaved", CurrentSession.Name);
        NotifySessionProperties();
    }

    [RelayCommand]
    public void NewSession()
    {
        if (IsPlaying) StopPlayback();
        if (IsRecording) StopRecording();

        _isInternalUpdate = true;
        CurrentSession = AutoClickManager.Instance.CreateNewSession(CreateDefaultSessionName("AutoClickDefaultSessionName"));
        RepeatCount = 1;
        PlaybackSpeed = 1.0;
        DefaultDelay = 100;
        RecordMouseMovement = AutoClickSettingConfig.Current.DefaultRecordMouseMovement;
        MouseMovementFrameRate = NormalizeMouseMovementFrameRate(AutoClickSettingConfig.Current.DefaultMouseMovementFrameRate);
        RecordMouseMovementOnlyWhenPressed = AutoClickSettingConfig.Current.DefaultRecordMouseMovementOnlyWhenPressed;
        CurrentSession.RecordMouseMovement = RecordMouseMovement;
        CurrentSession.MouseMovementFrameRate = MouseMovementFrameRate;
        CurrentSession.RecordMouseMovementOnlyWhenPressed = RecordMouseMovementOnlyWhenPressed;
        Steps.Clear();
        ReplaceVisibleSteps([]);
        SelectedStep = null;
        _isInternalUpdate = false;
        IsDirty = false;
        SetStatus("AutoClickStatusCreated", CurrentSession.Name);
        NotifyStepProperties();
        NotifySessionProperties();
    }

    [RelayCommand]
    public void DeleteSession(AutoClickSession? session)
    {
        if (session == null) return;
        AutoClickManager.Instance.Delete(session);
        if (CurrentSession == session)
        {
            _isInternalUpdate = true;
            CurrentSession = null;
            Steps.Clear();
            ReplaceVisibleSteps([]);
            SelectedStep = null;
            _isInternalUpdate = false;
            IsDirty = false;
            NotifyStepProperties();
            NotifySessionProperties();
        }

        SetStatus("AutoClickStatusDeleted", session.Name);
    }

    private static int GetSessionMouseMovementFrameRate(AutoClickSession session)
    {
        return NormalizeMouseMovementFrameRate(session.MouseMovementFrameRate > 0
            ? session.MouseMovementFrameRate
            : AutoClickSettingConfig.Current.DefaultMouseMovementFrameRate);
    }

    private static bool GetSessionRecordMouseMovementOnlyWhenPressed(AutoClickSession session)
    {
        return session.RecordMouseMovementOnlyWhenPressed ??
               AutoClickSettingConfig.Current.DefaultRecordMouseMovementOnlyWhenPressed;
    }

    private static int NormalizeMouseMovementFrameRate(int frameRate)
    {
        return Math.Clamp(frameRate, MinMouseMovementFrameRate, MaxMouseMovementFrameRate);
    }

    private int GetMouseMovementFrameIntervalMs()
    {
        return Math.Max(1, (int)Math.Round(1000.0 / NormalizeMouseMovementFrameRate(MouseMovementFrameRate)));
    }

    [RelayCommand]
    public void DuplicateSession(AutoClickSession? session)
    {
        if (session == null) return;
        var copy = new AutoClickSession
        {
            Version = AutoClickSession.CurrentVersion,
            Name = AutoClickManager.Instance.GetUniqueName($"{session.Name}_Copy"),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            RepeatCount = session.RepeatCount,
            PlaybackSpeed = session.PlaybackSpeed,
            DefaultDelay = session.DefaultDelay,
            RecordMouseMovement = session.RecordMouseMovement,
            MouseMovementFrameRate = GetSessionMouseMovementFrameRate(session),
            RecordMouseMovementOnlyWhenPressed = GetSessionRecordMouseMovementOnlyWhenPressed(session),
            Steps = session.Steps.Select(CloneData).ToList()
        };
        AutoClickManager.Instance.Add(copy);
        LoadSession(copy);
        SetStatus("AutoClickStatusDuplicated", copy.Name);
    }

    public void RenameSession(AutoClickSession session, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || session.Name == newName) return;
        var finalName = AutoClickManager.Instance.ModifyName(session, newName);
        session.UpdatedAt = DateTime.Now;
        RefreshSessionListItem(session);
        NotifySessionProperties();
        SetStatus("AutoClickStatusRenamed", finalName);
    }

    [RelayCommand]
    private void AddMouseClickStep() => InsertStep(CreateMouseStep(AutoClickStepKind.MouseClick, SharpHook.Data.MouseButton.Button1));

    [RelayCommand]
    private void AddMouseRightClickStep() => InsertStep(CreateMouseStep(AutoClickStepKind.MouseClick, SharpHook.Data.MouseButton.Button2));

    [RelayCommand]
    private void AddDelayStep() => InsertStep(new AutoClickStepViewModel(AutoClickStepKind.Delay, OnStepChanged) { Delay = Math.Max(0, DefaultDelay) });

    [RelayCommand]
    private void AddTextStep() => InsertStep(new AutoClickStepViewModel(AutoClickStepKind.Text, OnStepChanged) { Text = string.Empty, Delay = Math.Max(0, DefaultDelay) });

    [RelayCommand]
    private void AddLoopStep() => InsertStep(new AutoClickStepViewModel(AutoClickStepKind.Loop, OnStepChanged) { LoopCount = 2 });

    public void AddCustomKeyAction(KeyCode keyCode)
    {
        InsertStep(new AutoClickStepViewModel(AutoClickStepKind.KeyClick, OnStepChanged)
        {
            KeyCode = keyCode,
            Delay = Math.Max(0, DefaultDelay),
            Duration = 50
        });
        SetStatus("AutoClickStatusStepAdded");
    }

    [RelayCommand]
    private void RemoveSelectedStep() => RemoveStep(SelectedStep);

    [RelayCommand]
    private void RemoveStep(AutoClickStepViewModel? step)
    {
        if (step == null) return;
        RemoveFromParent(step);
        if (SelectedStep == step) SelectedStep = VisibleSteps.FirstOrDefault();
        MarkDirty();
        SetStatus("AutoClickStatusStepRemoved");
    }

    [RelayCommand]
    private void DuplicateSelectedStep()
    {
        if (SelectedStep == null) return;
        var copy = SelectedStep.Clone(OnStepChanged);
        InsertSiblingAfter(SelectedStep, copy);
        SelectedStep = copy;
        MarkDirty();
        SetStatus("AutoClickStatusStepDuplicated");
    }

    [RelayCommand]
    private void MoveSelectedStepUp()
    {
        if (SelectedStep == null) return;
        MoveStep(SelectedStep, -1);
    }

    [RelayCommand]
    private void MoveSelectedStepDown()
    {
        if (SelectedStep == null) return;
        MoveStep(SelectedStep, 1);
    }

    [RelayCommand]
    private void ClearSteps()
    {
        Steps.Clear();
        ReplaceVisibleSteps([]);
        SelectedStep = null;
        MarkDirty();
        SetStatus("AutoClickStatusCleared");
    }

    [RelayCommand]
    private void StartRecording()
    {
        if (IsRecording || IsPlaying) return;
        if (CurrentSession == null)
        {
            NewSession();
        }

        _shortcutSuspendScope = InputManager.Instance.SuspendRegisteredShortcuts();
        _onStartRecording?.Invoke();
        IsRecording = true;
        RecordedActionsCount = 0;
        _keyPressTimes.Clear();
        _recordStopwatch.Restart();
        _lastActionTime = DateTime.Now;
        _lastMousePosition = (0, 0);
        _mousePressPosition = (0, 0);
        _lastMouseMoveTime = DateTime.MinValue;
        _mousePressTime = DateTime.MinValue;
        _pendingMouseDownStep = null;
        _isMousePressed = false;
        _hasRecordedMouseMovementDuringPress = false;

        InputManager.Instance.EventOnKeyDown += OnRecordKeyDown;
        InputManager.Instance.EventOnKeyUp += OnRecordKeyUp;
        InputManager.Instance.EventOnMousePressed += OnRecordMousePressed;
        InputManager.Instance.EventOnMouseReleased += OnRecordMouseReleased;
        InputManager.Instance.EventOnMouseMoved += OnRecordMouseMoved;
        InputManager.Instance.EventOnMouseWheel += OnRecordMouseWheel;

        SetStatus("AutoClickStatusRecording");
    }

    [RelayCommand]
    private void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        _recordStopwatch.Stop();

        InputManager.Instance.EventOnKeyDown -= OnRecordKeyDown;
        InputManager.Instance.EventOnKeyUp -= OnRecordKeyUp;
        InputManager.Instance.EventOnMousePressed -= OnRecordMousePressed;
        InputManager.Instance.EventOnMouseReleased -= OnRecordMouseReleased;
        InputManager.Instance.EventOnMouseMoved -= OnRecordMouseMoved;
        InputManager.Instance.EventOnMouseWheel -= OnRecordMouseWheel;

        _shortcutSuspendScope?.Dispose();
        _shortcutSuspendScope = null;
        RemoveStopHotkeySteps();
        if (RecordedActionsCount > 0) MarkDirty();
        SetStatus("AutoClickStatusRecorded", RecordedActionsCount);
        _onStopRecording?.Invoke();
    }

    [RelayCommand]
    private async Task PlayActions()
    {
        if (IsPlaying || IsRecording || StepCount == 0) return;

        IsPlaying = true;
        _playbackCts = new CancellationTokenSource();
        SetStatus("AutoClickStatusPlaying");

        try
        {
            if (_onPreparePlayback != null)
            {
                await _onPreparePlayback(_playbackCts.Token);
            }

            await Task.Delay(PlaybackStartSettleDelayMs, _playbackCts.Token);

            var isInfinite = RepeatCount == 0;
            var loopIndex = 0;
            while (!_playbackCts.Token.IsCancellationRequested)
            {
                if (!isInfinite && loopIndex >= RepeatCount) break;
                _onPlaybackProgress?.Invoke(loopIndex + 1, RepeatCount);
                await ExecuteSteps(Steps, _playbackCts.Token);
                loopIndex++;
                if (!isInfinite && loopIndex >= RepeatCount) break;
                if (DefaultDelay > 0)
                {
                    await Task.Delay(GetAdjustedDelay(DefaultDelay), _playbackCts.Token);
                }
            }

            SetStatus("AutoClickStatusPlaybackFinished");
        }
        catch (OperationCanceledException)
        {
            SetStatus("AutoClickStatusPlaybackStopped");
        }
        catch (Exception e)
        {
            Log.Error(e);
            SetStatus("AutoClickStatusPlaybackFailed");
        }
        finally
        {
            foreach (var step in VisibleSteps) step.IsExecuting = false;
            IsPlaying = false;
            _playbackCts?.Dispose();
            _playbackCts = null;
            _onPlaybackFinished?.Invoke();
        }
    }

    [RelayCommand]
    private void StopPlayback()
    {
        _playbackCts?.Cancel();
    }

    public override void OnDisable()
    {
        base.OnDisable();
        if (IsRecording) StopRecording();
        _playbackCts?.Cancel();
    }

    partial void OnRepeatCountChanged(int value)
    {
        if (_isInternalUpdate) return;
        RepeatCount = Math.Max(0, value);
        MarkDirty();
    }

    partial void OnDefaultDelayChanged(int value)
    {
        if (_isInternalUpdate) return;
        DefaultDelay = Math.Max(0, value);
        MarkDirty();
    }

    partial void OnPlaybackSpeedChanged(double value)
    {
        if (_isInternalUpdate) return;
        PlaybackSpeed = Math.Clamp(value, 0.1, 5.0);
        MarkDirty();
    }

    partial void OnRecordMouseMovementChanged(bool value)
    {
        if (_isInternalUpdate) return;
        AutoClickSettingConfig.Current.DefaultRecordMouseMovement = value;
        MarkDirty();
    }

    partial void OnMouseMovementFrameRateChanged(int value)
    {
        if (_isInternalUpdate) return;
        MouseMovementFrameRate = NormalizeMouseMovementFrameRate(value);
        AutoClickSettingConfig.Current.DefaultMouseMovementFrameRate = MouseMovementFrameRate;
        MarkDirty();
    }

    partial void OnRecordMouseMovementOnlyWhenPressedChanged(bool value)
    {
        if (_isInternalUpdate) return;
        AutoClickSettingConfig.Current.DefaultRecordMouseMovementOnlyWhenPressed = value;
        MarkDirty();
    }

    partial void OnSelectedStepChanged(AutoClickStepViewModel? oldValue, AutoClickStepViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        NotifySelectedStepProperties();
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CurrentSessionDisplayName));
        OnPropertyChanged(nameof(UnsavedMarker));
        NotifySessionProperties();
    }

    partial void OnRecordedActionsCountChanged(int value)
    {
        OnPropertyChanged(nameof(RecordedActionsText));
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshFilteredSessions();
    }

    private void OnRootStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (AutoClickStepViewModel step in e.NewItems)
            {
                step.Parent = null;
            }
        }

        if (_isInternalUpdate)
        {
            return;
        }

        MarkDirty();
        RebuildVisibleSteps();
    }

    private void OnStepChanged(AutoClickStepViewModel step)
    {
        if (_isInternalUpdate || _isLoadingSession) return;
        MarkDirty();
        RebuildVisibleSteps();
    }

    private void OnSessionAdded(AutoClickSession session)
    {
        if (_allSessions.Any(x => x.Name == session.Name)) return;
        _allSessions.Insert(0, session);
        RefreshFilteredSessions();
    }

    private void OnSessionRemoved(AutoClickSession session)
    {
        _allSessions.Remove(session);
        SavedSessions.Remove(session);
    }

    private void InsertStep(AutoClickStepViewModel step)
    {
        if (SelectedStep?.IsLoop == true)
        {
            step.Parent = SelectedStep;
            SelectedStep.Children.Add(step);
        }
        else if (SelectedStep != null)
        {
            InsertSiblingAfter(SelectedStep, step);
        }
        else
        {
            Steps.Add(step);
        }

        SelectedStep = step;
        MarkDirty();
        SetStatus("AutoClickStatusStepAdded");
    }

    private void InsertSiblingAfter(AutoClickStepViewModel anchor, AutoClickStepViewModel step)
    {
        var collection = GetSiblingCollection(anchor);
        var index = collection.IndexOf(anchor);
        step.Parent = anchor.Parent;
        collection.Insert(index < 0 ? collection.Count : index + 1, step);
    }

    private ObservableCollection<AutoClickStepViewModel> GetSiblingCollection(AutoClickStepViewModel step)
    {
        return step.Parent?.Children ?? Steps;
    }

    private void RemoveFromParent(AutoClickStepViewModel step)
    {
        GetSiblingCollection(step).Remove(step);
        RebuildVisibleSteps();
    }

    private void MoveStep(AutoClickStepViewModel step, int delta)
    {
        var collection = GetSiblingCollection(step);
        var oldIndex = collection.IndexOf(step);
        if (oldIndex < 0) return;
        var newIndex = oldIndex + delta;
        if (newIndex < 0 || newIndex >= collection.Count) return;
        collection.Move(oldIndex, newIndex);
        MarkDirty();
        RebuildVisibleSteps();
    }

    private void RebuildVisibleSteps()
    {
        var selected = SelectedStep;
        var visibleSteps = new List<AutoClickStepViewModel>();
        AddVisibleSteps(Steps, 0);
        ReplaceVisibleSteps(visibleSteps);
        if (selected != null && !VisibleSteps.Contains(selected))
        {
            SelectedStep = VisibleSteps.FirstOrDefault();
        }

        NotifyStepProperties();

        void AddVisibleSteps(IEnumerable<AutoClickStepViewModel> steps, int level)
        {
            foreach (var step in steps)
            {
                step.Level = level;
                visibleSteps.Add(step);
                AddVisibleSteps(step.Children, level + 1);
            }
        }
    }

    private void ReplaceVisibleSteps(IEnumerable<AutoClickStepViewModel> steps)
    {
        VisibleSteps = new ObservableCollection<AutoClickStepViewModel>(steps);
        OnPropertyChanged(nameof(VisibleSteps));
    }

    private void MarkDirty()
    {
        if (_isInternalUpdate || _isLoadingSession) return;
        IsDirty = true;
        NotifyStepProperties();
    }

    private void NotifyStepProperties()
    {
        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(HasNoSteps));
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(StepCountText));
        NotifySelectedStepProperties();
    }

    private void NotifySelectedStepProperties()
    {
        OnPropertyChanged(nameof(HasSelectedStep));
        OnPropertyChanged(nameof(SelectedStepIsLoop));
        OnPropertyChanged(nameof(SelectedStepIsMouse));
        OnPropertyChanged(nameof(SelectedStepIsMouseButton));
        OnPropertyChanged(nameof(SelectedStepIsWheel));
        OnPropertyChanged(nameof(SelectedStepIsKeyboard));
        OnPropertyChanged(nameof(SelectedStepIsText));
        OnPropertyChanged(nameof(SelectedStepHasDuration));
    }

    private void NotifySessionProperties()
    {
        OnPropertyChanged(nameof(CurrentSessionName));
        OnPropertyChanged(nameof(CurrentSessionDisplayName));
        OnPropertyChanged(nameof(UnsavedMarker));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void RefreshLocalized()
    {
        StatusText = string.Format(LocalizationManager.Instance.GetString(_statusKey), _statusArgs);
        foreach (var step in VisibleSteps)
        {
            step.RefreshLocalized();
        }

        foreach (var option in MouseButtonOptions)
        {
            option.RefreshLocalized();
        }

        NotifySessionProperties();
        NotifyStepProperties();
    }

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText = string.Format(LocalizationManager.Instance.GetString(key), args);
    }

    private void RefreshSessionListItem(AutoClickSession session)
    {
        var allIndex = _allSessions.IndexOf(session);
        if (allIndex >= 0)
        {
            _allSessions.RemoveAt(allIndex);
            _allSessions.Insert(0, session);
        }
        else
        {
            _allSessions.Insert(0, session);
        }

        RefreshFilteredSessions();
    }

    private void RefreshFilteredSessions()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allSessions
            : _allSessions.Where(x => x.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        SavedSessions.Clear();
        foreach (var session in filtered)
        {
            SavedSessions.Add(session);
        }
    }

    private void MoveSessionToTop(AutoClickSession session)
    {
        var index = SavedSessions.IndexOf(session);
        if (index >= 0)
        {
            SavedSessions.RemoveAt(index);
            SavedSessions.Insert(index, session);
        }
        else
        {
            SavedSessions.Insert(0, session);
        }
    }

    private AutoClickStepViewModel CreateMouseStep(AutoClickStepKind kind, MouseButton button)
    {
        return new AutoClickStepViewModel(kind, OnStepChanged)
        {
            MouseButton = button,
            Delay = Math.Max(0, DefaultDelay),
            Duration = 50
        };
    }

    private void OnRecordKeyDown(KeyCode keyCode)
    {
        if (IsStopShortcutPressed(keyCode))
        {
            Dispatcher.UIThread.Post(StopRecording);
            return;
        }

        _keyPressTimes[keyCode] = DateTime.Now;
        var delay = CalculateDelay();
        Dispatcher.UIThread.Post(() => AddRecordedStep(CreateRecordedStep(AutoClickStepKind.KeyDown, step =>
        {
            step.KeyCode = keyCode;
            step.Delay = delay;
        })));
    }

    private void OnRecordKeyUp(KeyCode keyCode)
    {
        var now = DateTime.Now;
        int? duration = null;
        if (_keyPressTimes.TryGetValue(keyCode, out var pressTime))
        {
            duration = (int)(now - pressTime).TotalMilliseconds;
            _keyPressTimes.Remove(keyCode);
        }

        var delay = (int)(now - _lastActionTime).TotalMilliseconds;
        _lastActionTime = now;
        if (duration is > 0 and <= KeyClickMergeThresholdMs)
        {
            Dispatcher.UIThread.Post(() => ReplacePendingKeyDownWithPress(keyCode, duration.Value));
            return;
        }

        Dispatcher.UIThread.Post(() => AddRecordedStep(CreateRecordedStep(AutoClickStepKind.KeyUp, step =>
        {
            step.KeyCode = keyCode;
            step.Delay = Math.Max(0, delay);
            step.Duration = duration;
        })));
    }

    private void ReplacePendingKeyDownWithPress(KeyCode keyCode, int duration)
    {
        var lastStep = VisibleSteps.LastOrDefault();
        if (lastStep?.Kind != AutoClickStepKind.KeyDown || lastStep.KeyCode != keyCode)
        {
            AddRecordedStep(CreateRecordedStep(AutoClickStepKind.KeyClick, step =>
            {
                step.KeyCode = keyCode;
                step.Duration = duration;
            }));
            return;
        }

        var delay = lastStep.Delay;
        RemoveFromParent(lastStep);
        RecordedActionsCount = Math.Max(0, RecordedActionsCount - 1);
        AddRecordedStep(CreateRecordedStep(AutoClickStepKind.KeyClick, step =>
        {
            step.KeyCode = keyCode;
            step.Delay = delay;
            step.Duration = duration;
        }));
    }

    private void OnRecordMousePressed(MouseEventData mouseData)
    {
        _isMousePressed = true;
        _mousePressTime = DateTime.Now;
        _lastMousePosition = (mouseData.X, mouseData.Y);
        _mousePressPosition = (mouseData.X, mouseData.Y);
        _hasRecordedMouseMovementDuringPress = false;
        var delay = CalculateDelay();
        _pendingMouseDownStep = CreateRecordedStep(AutoClickStepKind.MouseDown, step =>
        {
            step.MouseButton = mouseData.Button;
            step.X = mouseData.X;
            step.Y = mouseData.Y;
            step.Delay = delay;
        });
    }

    private void OnRecordMouseReleased(MouseEventData mouseData)
    {
        _isMousePressed = false;
        var now = DateTime.Now;
        var duration = _mousePressTime == DateTime.MinValue
            ? 0
            : (int)(now - _mousePressTime).TotalMilliseconds;
        var releaseDx = Math.Abs(mouseData.X - _mousePressPosition.x);
        var releaseDy = Math.Abs(mouseData.Y - _mousePressPosition.y);
        var isClick = duration is > 0 and <= MouseClickMergeThresholdMs &&
                      !_hasRecordedMouseMovementDuringPress &&
                      releaseDx < MouseMoveRecordMinDistance &&
                      releaseDy < MouseMoveRecordMinDistance;

        if (isClick)
        {
            var clickDelay = _pendingMouseDownStep?.Delay ?? Math.Max(0, (int)(now - _lastActionTime).TotalMilliseconds);
            _pendingMouseDownStep = null;
            _lastActionTime = now;
            Dispatcher.UIThread.Post(() => AddRecordedStep(CreateRecordedStep(AutoClickStepKind.MouseClick, step =>
            {
                step.MouseButton = mouseData.Button;
                step.X = mouseData.X;
                step.Y = mouseData.Y;
                step.Delay = clickDelay;
                step.Duration = duration;
            })));
            return;
        }

        FlushPendingMouseDown();
        AddFinalRecordedMouseMoveIfNeeded(mouseData, now);
        now = DateTime.Now;
        var delay = (int)(now - _lastActionTime).TotalMilliseconds;
        _lastActionTime = now;

        Dispatcher.UIThread.Post(() => AddRecordedStep(CreateRecordedStep(AutoClickStepKind.MouseUp, step =>
        {
            step.MouseButton = mouseData.Button;
            step.X = mouseData.X;
            step.Y = mouseData.Y;
            step.Delay = Math.Max(0, delay);
            step.Duration = duration > 0 ? duration : null;
        })));
    }

    private void OnRecordMouseMoved(MouseEventData mouseData)
    {
        if (!RecordMouseMovement) return;
        if (RecordMouseMovementOnlyWhenPressed && !_isMousePressed) return;
        var dx = Math.Abs(mouseData.X - _lastMousePosition.x);
        var dy = Math.Abs(mouseData.Y - _lastMousePosition.y);
        var now = DateTime.Now;
        if ((dx < MouseMoveRecordMinDistance && dy < MouseMoveRecordMinDistance) ||
            (now - _lastMouseMoveTime).TotalMilliseconds < GetMouseMovementFrameIntervalMs())
        {
            return;
        }

        var delay = CalculateDelay();
        FlushPendingMouseDown();
        QueueRecordedMouseMove(mouseData, delay, now);
    }

    private void AddFinalRecordedMouseMoveIfNeeded(MouseEventData mouseData, DateTime now)
    {
        if (!RecordMouseMovement) return;
        if (mouseData.X == _lastMousePosition.x && mouseData.Y == _lastMousePosition.y) return;
        FlushPendingMouseDown();
        QueueRecordedMouseMove(mouseData, CalculateDelay(), now);
    }

    private void FlushPendingMouseDown()
    {
        if (_pendingMouseDownStep == null) return;
        var step = _pendingMouseDownStep;
        _pendingMouseDownStep = null;
        Dispatcher.UIThread.Post(() => AddRecordedStep(step));
    }

    private void QueueRecordedMouseMove(MouseEventData mouseData, int delay, DateTime now)
    {
        if (_isMousePressed)
        {
            _hasRecordedMouseMovementDuringPress = true;
        }

        _lastMousePosition = (mouseData.X, mouseData.Y);
        _lastMouseMoveTime = now;
        Dispatcher.UIThread.Post(() => AddRecordedStep(CreateRecordedStep(AutoClickStepKind.MouseMove, step =>
        {
            step.X = mouseData.X;
            step.Y = mouseData.Y;
            step.Delay = delay;
        })));
    }

    private void OnRecordMouseWheel(MouseWheelEventData wheelData)
    {
        var delay = CalculateDelay();
        var wheelSteps = NormalizeWheelSteps(wheelData);
        Dispatcher.UIThread.Post(() => AddRecordedStep(CreateRecordedStep(AutoClickStepKind.MouseWheel, step =>
        {
            step.WheelDelta = wheelSteps;
            step.Delay = delay;
        })));
    }

    private static int NormalizeWheelSteps(MouseWheelEventData wheelData)
    {
        var rotation = wheelData.Rotation;
        if (rotation == 0) return 0;

        var delta = wheelData.Delta;
        if (delta > 0)
        {
            return (int)Math.Round((double)rotation / delta, MidpointRounding.AwayFromZero);
        }

        return rotation;
    }

    private AutoClickStepViewModel CreateRecordedStep(
        AutoClickStepKind kind,
        Action<AutoClickStepViewModel> initialize)
    {
        return AutoClickStepViewModel.CreateInitialized(kind, OnStepChanged, initialize);
    }

    private void AddRecordedStep(AutoClickStepViewModel step)
    {
        Steps.Add(step);
        SelectedStep = step;
        RecordedActionsCount++;
        OnActionRecorded?.Invoke(RecordedActionsCount);
    }

    private int CalculateDelay()
    {
        var now = DateTime.Now;
        var delay = (int)(now - _lastActionTime).TotalMilliseconds;
        _lastActionTime = now;
        return Math.Max(0, delay);
    }

    private bool IsStopShortcutPressed(KeyCode mainKey)
    {
        if (!ShortcutGestureParser.TryParse(ConfigManager.Instance.Setting.QuickAutoClickShortcut, out var shortcutMainKey,
                out var modifiers, out _))
        {
            return false;
        }

        if (shortcutMainKey != mainKey) return false;
        return modifiers.All(InputManager.Instance.IsPressed);
    }

    private void RemoveStopHotkeySteps()
    {
        if (!ShortcutGestureParser.TryParse(ConfigManager.Instance.Setting.QuickAutoClickShortcut, out var mainKey,
                out var modifiers, out _))
        {
            return;
        }

        var shortcutKeys = new HashSet<KeyCode>(modifiers) { mainKey };
        var tail = VisibleSteps
            .Reverse()
            .TakeWhile(step => IsShortcutKeyboardStep(step, shortcutKeys))
            .ToList();
        if (tail.Count == 0) return;

        foreach (var step in tail)
        {
            RemoveFromParent(step);
            if (step.KeyCode.HasValue)
            {
                _keyPressTimes.Remove(step.KeyCode.Value);
            }
        }

        RecordedActionsCount = Math.Max(0, RecordedActionsCount - tail.Count);
    }

    private static bool IsShortcutKeyboardStep(AutoClickStepViewModel step, ISet<KeyCode> shortcutKeys)
    {
        return step.Kind is AutoClickStepKind.KeyDown or AutoClickStepKind.KeyUp or AutoClickStepKind.KeyClick &&
               step.KeyCode.HasValue &&
               shortcutKeys.Contains(step.KeyCode.Value);
    }

    private async Task ExecuteSteps(IEnumerable<AutoClickStepViewModel> steps, CancellationToken token)
    {
        var stepList = steps.ToList();
        for (var index = 0; index < stepList.Count; index++)
        {
            var step = stepList[index];
            token.ThrowIfCancellationRequested();
            if (!step.IsEnabled) continue;
            if (step.Kind == AutoClickStepKind.MouseMove)
            {
                index = await ExecuteMouseMoveRun(stepList, index, token);
                continue;
            }

            var showExecutingState = step.Kind != AutoClickStepKind.MouseMove;
            if (showExecutingState)
            {
                step.IsExecuting = true;
            }

            if (step.Delay > 0)
            {
                await Task.Delay(GetAdjustedDelay(step.Delay), token);
            }

            if (step.Kind == AutoClickStepKind.Loop)
            {
                for (var i = 0; i < Math.Max(1, step.LoopCount); i++)
                {
                    await ExecuteSteps(step.Children, token);
                }
            }
            else
            {
                await ExecuteStep(step, token);
            }

            if (showExecutingState)
            {
                step.IsExecuting = false;
            }
        }
    }

    private async Task<int> ExecuteMouseMoveRun(IReadOnlyList<AutoClickStepViewModel> steps, int startIndex, CancellationToken token)
    {
        var points = new List<(short X, short Y, int Delay)>();
        var index = startIndex;

        for (; index < steps.Count; index++)
        {
            var step = steps[index];
            if (!step.IsEnabled || step.Kind != AutoClickStepKind.MouseMove) break;
            if (step.X == 0 && step.Y == 0) continue;
            points.Add((step.X, step.Y, Math.Max(0, step.Delay)));
        }

        await PlayMouseMovePath(points, token);
        return index - 1;
    }

    private async Task PlayMouseMovePath(IReadOnlyList<(short X, short Y, int Delay)> points, CancellationToken token)
    {
        if (points.Count == 0) return;

        var stopwatch = Stopwatch.StartNew();
        var targetElapsedMs = 0;
        var mouseMovePlaybackFrameIntervalMs = GetMouseMovementFrameIntervalMs();
        var lastSentTargetMs = -mouseMovePlaybackFrameIntervalMs;

        for (var i = 0; i < points.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var point = points[i];
            targetElapsedMs += GetAdjustedDelayOrZero(point.Delay);

            var isLast = i == points.Count - 1;
            if (!isLast && targetElapsedMs - lastSentTargetMs < mouseMovePlaybackFrameIntervalMs)
            {
                continue;
            }

            await DelayUntil(stopwatch, targetElapsedMs, token);
            InputSimulateManager.Instance.SendMouseMove(point.X, point.Y);
            lastSentTargetMs = targetElapsedMs;
        }
    }

    private static async Task DelayUntil(Stopwatch stopwatch, int targetElapsedMs, CancellationToken token)
    {
        var remainingMs = targetElapsedMs - stopwatch.ElapsedMilliseconds;
        if (remainingMs > 1)
        {
            await Task.Delay((int)remainingMs, token);
        }
    }

    private async Task ExecuteStep(AutoClickStepViewModel step, CancellationToken token)
    {
        switch (step.Kind)
        {
            case AutoClickStepKind.MouseClick:
                if (step.MouseButton.HasValue)
                {
                    await MoveMouseIfNeeded(step, token, true);
                    await InputSimulateManager.Instance.SendMouseClick(step.MouseButton.Value, step.Duration ?? 10);
                }

                break;
            case AutoClickStepKind.MouseDown:
                if (step.MouseButton.HasValue)
                {
                    await MoveMouseIfNeeded(step, token, true);
                    InputSimulateManager.Instance.SimulateMousePress(step.MouseButton.Value);
                    var mouseDownDuration = step.Duration.GetValueOrDefault();
                    if (mouseDownDuration > 0)
                    {
                        await Task.Delay(GetAdjustedDelay(mouseDownDuration), token);
                    }
                }

                break;
            case AutoClickStepKind.MouseUp:
                if (step.MouseButton.HasValue)
                {
                    await MoveMouseIfNeeded(step, token, true);
                    InputSimulateManager.Instance.SimulateMouseRelease(step.MouseButton.Value);
                }

                break;
            case AutoClickStepKind.MouseMove:
                await MoveMouseIfNeeded(step, token, false);
                break;
            case AutoClickStepKind.MouseWheel:
                if (step.WheelDelta.HasValue)
                {
                    InputSimulateManager.Instance.SimulateMouseWheel(step.WheelDelta.Value);
                }

                break;
            case AutoClickStepKind.KeyClick:
                if (step.KeyCode.HasValue)
                {
                    await InputSimulateManager.Instance.SendKeyPress(step.KeyCode.Value, step.Duration ?? 50);
                }

                break;
            case AutoClickStepKind.KeyDown:
                if (step.KeyCode.HasValue)
                {
                    InputSimulateManager.Instance.SimulateKeyPress(step.KeyCode.Value);
                    var keyDownDuration = step.Duration.GetValueOrDefault();
                    if (keyDownDuration > 0)
                    {
                        await Task.Delay(GetAdjustedDelay(keyDownDuration), token);
                    }
                }

                break;
            case AutoClickStepKind.KeyUp:
                if (step.KeyCode.HasValue)
                {
                    InputSimulateManager.Instance.SimulateKeyRelease(step.KeyCode.Value);
                }

                break;
            case AutoClickStepKind.Text:
                if (!string.IsNullOrEmpty(step.Text))
                {
                    await InputSimulateManager.Instance.SendText(step.Text, 50);
                }

                break;
            case AutoClickStepKind.Delay:
                if (step.Delay > 0)
                {
                    await Task.Delay(GetAdjustedDelay(step.Delay), token);
                }

                break;
        }
    }

    private async Task MoveMouseIfNeeded(AutoClickStepViewModel step, CancellationToken token, bool settleBeforeAction)
    {
        if (step.X != 0 || step.Y != 0)
        {
            InputSimulateManager.Instance.SendMouseMove(step.X, step.Y);
            if (settleBeforeAction)
            {
                await Task.Delay(MouseMoveSettleDelayMs, token);
            }
        }
    }

    private int GetAdjustedDelay(int delay)
    {
        return Math.Max(1, (int)(delay / Math.Clamp(PlaybackSpeed, 0.1, 5.0)));
    }

    private int GetAdjustedDelayOrZero(int delay)
    {
        if (delay <= 0) return 0;
        return GetAdjustedDelay(delay);
    }

    private int CountSteps(IEnumerable<AutoClickStepViewModel> steps)
    {
        return steps.Sum(step => 1 + CountSteps(step.Children));
    }

    private static AutoClickStepData CloneData(AutoClickStepData data)
    {
        return new AutoClickStepData
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = data.Kind,
            IsEnabled = data.IsEnabled,
            Delay = data.Delay,
            MouseButton = data.MouseButton,
            X = data.X,
            Y = data.Y,
            KeyCode = data.KeyCode,
            Text = data.Text,
            WheelDelta = data.WheelDelta,
            Duration = data.Duration,
            LoopCount = data.LoopCount,
            Children = data.Children.Select(CloneData).ToList()
        };
    }

    private string CreateDefaultSessionName(string key)
    {
        return $"{LocalizationManager.Instance.GetString(key)}_{DateTime.Now:yyyyMMdd_HHmmss}";
    }
}

public partial class MouseButtonOption : ObservableObject
{
    private readonly string _displayNameKey;

    public MouseButtonOption(MouseButton value, string displayNameKey)
    {
        Value = value;
        _displayNameKey = displayNameKey;
        DisplayName = LocalizationManager.Instance.GetString(displayNameKey);
    }

    public MouseButton Value { get; }

    [ObservableProperty] private string _displayName;

    public void RefreshLocalized()
    {
        DisplayName = LocalizationManager.Instance.GetString(_displayNameKey);
    }
}
