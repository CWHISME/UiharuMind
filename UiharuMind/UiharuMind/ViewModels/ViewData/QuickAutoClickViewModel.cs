using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpHook.Data;
using UiharuMind.Core.AutoClick;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.ViewModels.Extensions;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 自动点击录制动作数据
/// </summary>
public partial class AutoClickAction : ObservableObject
{
    [ObservableProperty] private string _actionType; // MouseClick, MouseDown, MouseUp, MouseMove, MouseWheel, KeyPress, KeyDown, KeyUp, Text, Delay
    [ObservableProperty] private string _description;
    [ObservableProperty] private int _delay; // 延迟时间（毫秒）
    
    // 鼠标相关
    [ObservableProperty] private MouseButton? _mouseButton;
    [ObservableProperty] private short _mouseX;
    [ObservableProperty] private short _mouseY;
    
    // 键盘相关
    [ObservableProperty] private KeyCode? _keyCode;
    [ObservableProperty] private string? _text;
    
    // 滚轮相关
    [ObservableProperty] private int? _wheelDelta;
    
    // 持续时间（用于长按/按住）
    [ObservableProperty] private int? _duration;

    public AutoClickAction(string actionType, string description, int delay = 0)
    {
        _actionType = actionType;
        _description = description;
        _delay = delay;
    }
}

/// <summary>
/// 快速自动点击窗口视图模型
/// </summary>
public partial class QuickAutoClickViewModel : ViewModelBase
{
    private CancellationTokenSource? _playbackCts;
    private Stopwatch? _recordStopwatch;
    private DateTime _lastActionTime;
    private Action? _onStartRecording;
    private Action? _onStopRecording;
    private Action<int, int>? _onPlaybackProgress;
    private Action? _onPlaybackFinished;
    private HashSet<KeyCode> _pressedKeys = new();
    private Dictionary<KeyCode, DateTime> _keyPressTimes = new(); // 记录按键按下时间
    private (short x, short y) _lastMousePosition = (0, 0); // 记录上次鼠标位置
    private bool _isMousePressed = false; // 鼠标是否处于按下状态
    private bool _shouldRecordMouseDownUp = false; // 是否应该记录 MouseDown/Up（用于区分点击和长按）
    
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private int _repeatCount = 1;
    [ObservableProperty] private int _defaultDelay = 100;
    [ObservableProperty] private double _playbackSpeed = 1.0; // 播放倍速
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private int _recordedActionsCount = 0;
    [ObservableProperty] private AutoClickSession? _currentSession;
    [ObservableProperty] private bool _isDirty; // 会话是否被修改过
    
    /// <summary>
    /// 当前会话名称（如果有）
    /// </summary>
    public string CurrentSessionName => _currentSession?.Name ?? "未命名会话";
    
    public ObservableCollection<AutoClickAction> Actions { get; } = new();
    public ObservableCollection<AutoClickSession> SavedSessions { get; } = new();

    public int ActionCount => Actions.Count;
    
    public bool HasNoActions => Actions.Count == 0;
    public bool HasActions => Actions.Count > 0;
    
    /// <summary>
    /// 是否有未保存的修改（有动作且是脏状态）
    /// </summary>
    public bool HasUnsavedChanges => IsDirty && Actions.Count > 0;

    public event Action<int>? OnActionRecorded;

    public QuickAutoClickViewModel()
    {
        Actions.CollectionChanged += OnActionsCollectionChanged;
        
        // 监听 AutoClickManager 的事件
        AutoClickManager.Instance.OnItemAdded += OnSessionAdded;
        AutoClickManager.Instance.OnItemRemoved += OnSessionRemoved;
        
        // 加载已保存的会话
        LoadSavedSessions();
    }
    
    /// <summary>
    /// 会话添加事件处理
    /// </summary>
    private void OnSessionAdded(AutoClickSession session)
    {
        // 检查是否已经存在
        if (SavedSessions.Any(s => s.Name == session.Name))
            return;
        
        // 插入到最前面（按时间排序）
        SavedSessions.Insert(0, session);
    }
    
    /// <summary>
    /// 会话删除事件处理
    /// </summary>
    private void OnSessionRemoved(AutoClickSession session)
    {
        SavedSessions.Remove(session);
        
        // 如果删除的是当前会话，清空引用
        if (CurrentSession == session)
        {
            Actions.Clear();
            CurrentSession = null;
            IsDirty = false;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(CurrentSessionName));
        }
    }

    /// <summary>
    /// 加载已保存的会话列表
    /// </summary>
    public void LoadSavedSessions()
    {
        SavedSessions.Clear();
        var sessions = AutoClickManager.Instance.GetOrderedItems();
        foreach (var session in sessions)
        {
            SavedSessions.Add(session);
        }
    }

    public void SetRecordingCallbacks(Action? onStart, Action? onStop)
    {
        _onStartRecording = onStart;
        _onStopRecording = onStop;
    }

    public void SetPlaybackCallback(Action<int, int>? onProgress, Action? onFinished = null)
    {
        _onPlaybackProgress = onProgress;
        _onPlaybackFinished = onFinished;
    }

    private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ActionCount));
        OnPropertyChanged(nameof(HasNoActions));
        OnPropertyChanged(nameof(HasActions));
        
        // 如果动作发生变化（非Reset），标记为脏
        // Reset 是 Clear() 触发的，不应该标记为脏
        if (e.Action != NotifyCollectionChangedAction.Reset && Actions.Count > 0)
        {
            IsDirty = true;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
        else if (Actions.Count == 0)
        {
            // 如果清空了所有动作，重置脏标记
            IsDirty = false;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    #region 录制功能

    [RelayCommand]
    private void StartRecording()
    {
        if (IsRecording) return;
        
        // 如果当前有未保存的动作或没有当前会话，创建新会话
        if (Actions.Count > 0 || CurrentSession == null)
        {
            Actions.Clear();
            var newSession = AutoClickManager.Instance.CreateNewSession($"录制_{DateTime.Now:yyyyMMdd_HHmmss}");
            CurrentSession = newSession;
            IsDirty = false;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(CurrentSessionName));
        }
        
        // 隐藏窗口
        _onStartRecording?.Invoke();
        
        IsRecording = true;
        RecordedActionsCount = 0;
        _pressedKeys.Clear();
        _keyPressTimes.Clear(); // 清空按键按下时间记录
        _recordStopwatch = new Stopwatch();
        _recordStopwatch.Start();
        _lastActionTime = DateTime.Now;
        _lastMousePosition = (0, 0); // 重置鼠标位置
        
        // 注册全局事件监听
        InputManager.Instance.EventOnKeyDown += OnRecordKeyDown;
        InputManager.Instance.EventOnKeyUp += OnRecordKeyUp;
        InputManager.Instance.EventOnMouseClicked += OnRecordMouseClick;
        InputManager.Instance.EventOnMousePressed += OnRecordMousePressed;
        InputManager.Instance.EventOnMouseReleased += OnRecordMouseReleased;
        InputManager.Instance.EventOnMouseMoved += OnRecordMouseMoved;
        InputManager.Instance.EventOnMouseWheel += OnRecordMouseWheel;
        
        StatusText = "🔴 录制中... 操作鼠标和键盘";
        Log.Debug("开始录制动作");
    }

    [RelayCommand]
    private void StopRecording()
    {
        if (!IsRecording) return;
        
        IsRecording = false;
        _recordStopwatch?.Stop();
        
        // 取消注册全局事件监听
        InputManager.Instance.EventOnKeyDown -= OnRecordKeyDown;
        InputManager.Instance.EventOnKeyUp -= OnRecordKeyUp;
        InputManager.Instance.EventOnMouseClicked -= OnRecordMouseClick;
        InputManager.Instance.EventOnMousePressed -= OnRecordMousePressed;
        InputManager.Instance.EventOnMouseReleased -= OnRecordMouseReleased;
        InputManager.Instance.EventOnMouseMoved -= OnRecordMouseMoved;
        InputManager.Instance.EventOnMouseWheel -= OnRecordMouseWheel;
        
        // 如果有录制的动作，标记为脏
        if (RecordedActionsCount > 0)
        {
            IsDirty = true;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
        
        StatusText = $"✅ 录制完成，共 {RecordedActionsCount} 个动作";
        Log.Debug($"停止录制，共录制 {RecordedActionsCount} 个动作");
        
        // 显示窗口
        _onStopRecording?.Invoke();
    }

    private void OnRecordKeyDown(KeyCode keyCode)
    {
        // InputManager 已经过滤了键盘重复事件，这里直接记录
        
        // 记录按下时间
        _keyPressTimes[keyCode] = DateTime.Now;
        
        // 如果按下的是停止快捷键（Alt+Shift+G），不录制并停止
        if (keyCode == KeyCode.VcG && 
            InputManager.Instance.IsPressed(KeyCode.VcLeftAlt) && 
            InputManager.Instance.IsPressed(KeyCode.VcLeftShift))
        {
            StopRecording();
            return;
        }
        
        // 记录所有按键（包括修饰键）
        var delay = CalculateDelay();
        var action = new AutoClickAction("KeyDown", GetKeyCodeDescription(keyCode), delay)
        {
            KeyCode = keyCode
        };

        Dispatcher.UIThread.Post(() => { AddAction(action); });
    }

    private void OnRecordKeyUp(KeyCode keyCode)
    {
        _pressedKeys.Remove(keyCode);
        
        // 计算按下时长
        int? duration = null;
        if (_keyPressTimes.TryGetValue(keyCode, out var pressTime))
        {
            duration = (int)(DateTime.Now - pressTime).TotalMilliseconds;
            _keyPressTimes.Remove(keyCode);
        }
        
        var delay = CalculateDelay();
        var action = new AutoClickAction("KeyUp", GetKeyCodeDescription(keyCode), delay)
        {
            KeyCode = keyCode,
            Duration = duration
        };

        Dispatcher.UIThread.Post(() => { AddAction(action); });
    }

    private void OnRecordMouseClick(MouseEventData mouseData)
    {
        // InputManager 已经判断为短按（<=300ms）
        // 如果是短按，删除刚才记录的 MouseDown，不记录 MouseUp，只记录 MouseClick
        if (_shouldRecordMouseDownUp && Actions.Count > 0)
        {
            // 移除最后一个动作（MouseDown）
            var lastAction = Actions.Last();
            if (lastAction.ActionType == "MouseDown")
            {
                Actions.RemoveAt(Actions.Count - 1);
                RecordedActionsCount--;
            }
            _shouldRecordMouseDownUp = false; // 标记为不需要记录 MouseUp
        }
        
        // 记录 MouseClick
        var delay = CalculateDelay();
        var button = GetMouseButtonFromData(mouseData);
        var action = new AutoClickAction("MouseClick", GetMouseButtonDescription(button), delay)
        {
            MouseButton = button,
            MouseX = mouseData.X,
            MouseY = mouseData.Y
        };

        Dispatcher.UIThread.Post(() => { AddAction(action); });
    }

    private void OnRecordMousePressed(MouseEventData mouseData)
    {
        _isMousePressed = true;
        _shouldRecordMouseDownUp = true; // 默认假设是长按，需要记录
        
        // 更新最后鼠标位置为按下时的位置
        _lastMousePosition = (mouseData.X, mouseData.Y);
        
        var delay = CalculateDelay();
        var button = GetMouseButtonFromData(mouseData);
        var action = new AutoClickAction("MouseDown", $"鼠标{GetMouseButtonDescription(button)}按下", delay)
        {
            MouseButton = button,
            MouseX = mouseData.X,
            MouseY = mouseData.Y
        };

        Dispatcher.UIThread.Post(() => { AddAction(action); });
    }

    private void OnRecordMouseReleased(MouseEventData mouseData)
    {
        _isMousePressed = false;
        
        // 如果标记为不应该记录（即会触发 Click），则跳过
        if (!_shouldRecordMouseDownUp)
        {
            _shouldRecordMouseDownUp = true; // 重置标记
            return;
        }
        
        _shouldRecordMouseDownUp = true; // 重置标记
        
        var delay = CalculateDelay();
        var button = GetMouseButtonFromData(mouseData);
        var action = new AutoClickAction("MouseUp", $"鼠标{GetMouseButtonDescription(button)}释放", delay)
        {
            MouseButton = button,
            MouseX = mouseData.X,
            MouseY = mouseData.Y
        };

        Dispatcher.UIThread.Post(() => { AddAction(action); });
    }

    private void OnRecordMouseMoved(MouseEventData mouseData)
    {
        // 只在鼠标按下时记录移动（拖拽操作）
        if (!_isMousePressed) return;
        
        // 只记录位置变化较大的移动（避免过多动作）
        var dx = Math.Abs(mouseData.X - _lastMousePosition.x);
        var dy = Math.Abs(mouseData.Y - _lastMousePosition.y);
        
        if (dx < 5 && dy < 5) return; // 忽略微小移动
        
        var delay = CalculateDelay();
        var action = new AutoClickAction("MouseMove", $"鼠标移动到 ({mouseData.X}, {mouseData.Y})", delay)
        {
            MouseX = mouseData.X,
            MouseY = mouseData.Y
        };

        _lastMousePosition = (mouseData.X, mouseData.Y);
        Dispatcher.UIThread.Post(() => { AddAction(action); });
    }

    private void OnRecordMouseWheel(MouseWheelEventData wheelData)
    {
        var delay = CalculateDelay();
        var direction = wheelData.Rotation > 0 ? "向上" : "向下";
        var action = new AutoClickAction("MouseWheel", $"鼠标滚轮{direction}", delay)
        {
            WheelDelta = wheelData.Rotation
        };

        Dispatcher.UIThread.Post(() => { AddAction(action); });
    }

    private int CalculateDelay()
    {
        var now = DateTime.Now;
        var delay = (int)(now - _lastActionTime).TotalMilliseconds;
        _lastActionTime = now;
        return Math.Max(0, delay);
    }

    private void AddAction(AutoClickAction action)
    {
        Actions.Add(action);
        RecordedActionsCount++;
        OnActionRecorded?.Invoke(RecordedActionsCount);
    }

    private bool IsModifierKey(KeyCode keyCode)
    {
        return keyCode is KeyCode.VcLeftShift or KeyCode.VcRightShift or
                     KeyCode.VcLeftControl or KeyCode.VcRightControl or
                     KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
                     KeyCode.VcLeftMeta or KeyCode.VcRightMeta;
    }

    private MouseButton GetMouseButtonFromData(MouseEventData data)
    {
        return data.Button;
    }

    private string GetKeyCodeDescription(KeyCode keyCode)
    {
        return keyCode.ToString().Replace("Vc", "");
    }

    private string GetMouseButtonDescription(MouseButton button)
    {
        return button switch
        {
            MouseButton.Button1 => "鼠标左键",
            MouseButton.Button2 => "鼠标右键",
            MouseButton.Button3 => "鼠标中键",
            _ => "鼠标点击"
        };
    }

    #endregion
    
    [RelayCommand]
    private async Task PlayActions()
    {
        if (IsPlaying || Actions.Count == 0) return;
        
        IsPlaying = true;
        StatusText = "▶ 执行中...";
        _playbackCts = new CancellationTokenSource();
        
        try
        {
            for (int i = 0; i < RepeatCount; i++)
            {
                if (_playbackCts.Token.IsCancellationRequested) break;
                
                if (RepeatCount > 1)
                {
                    StatusText = $"▶ 执行中... ({i + 1}/{RepeatCount})";
                }
                
                // 通知进度
                _onPlaybackProgress?.Invoke(i + 1, RepeatCount);
                
                foreach (var action in Actions)
                {
                    if (_playbackCts.Token.IsCancellationRequested) break;
                    
                    // 先等待延迟时间（根据倍速调整）
                    if (action.Delay > 0)
                    {
                        var adjustedDelay = (int)(action.Delay / PlaybackSpeed);
                        await Task.Delay(Math.Max(10, adjustedDelay), _playbackCts.Token);
                    }
                    
                    // 再执行动作
                    await ExecuteAction(action);
                }
            }
            
            StatusText = "✅ 执行完成";
        }
        catch (OperationCanceledException)
        {
            StatusText = "⏹ 已停止";
        }
        catch (Exception e)
        {
            Log.Error(e);
            StatusText = "❌ 执行出错";
        }
        finally
        {
            IsPlaying = false;
            _playbackCts?.Dispose();
            _playbackCts = null;
            
            // 通知回放完成
            _onPlaybackFinished?.Invoke();
        }
    }

    [RelayCommand]
    private void StopPlayback()
    {
        _playbackCts?.Cancel();
    }

    [RelayCommand]
    private void ClearActions()
    {
        Actions.Clear();
        StatusText = "已清空";
    }

    [RelayCommand]
    private void RemoveAction(AutoClickAction? action)
    {
        if (action != null)
        {
            Actions.Remove(action);
            StatusText = "已删除动作";
        }
    }

    #region 保存和加载会话

    public void SaveSession()
    {
        if (Actions.Count == 0)
        {
            StatusText = "❌ 没有可保存的动作";
            return;
        }

        // 如果没有当前会话，创建一个新的
        if (CurrentSession == null)
        {
            CurrentSession = AutoClickManager.Instance.CreateNewSession($"录制_{DateTime.Now:yyyyMMdd_HHmmss}");
        }

        // 更新会话数据
        CurrentSession.Actions = Actions.ToDataList();
        CurrentSession.RepeatCount = RepeatCount;
        CurrentSession.DefaultDelay = DefaultDelay;
        CurrentSession.LastTime = DateTime.Now;

        // 保存到文件
        AutoClickManager.Instance.Save(CurrentSession);
        
        // 重置脏标记（已保存）
        IsDirty = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CurrentSessionName));
        
        // 通知 UI 刷新该会话项（因为 LastTime 等属性改变了）
        var index = SavedSessions.IndexOf(CurrentSession);
        if (index >= 0)
        {
            // 移除再重新添加，触发 UI 更新
            SavedSessions.RemoveAt(index);
            SavedSessions.Insert(index, CurrentSession);
        }

        StatusText = $"✅ 已保存: {CurrentSession.Name}";
        Log.Debug($"保存会话: {CurrentSession.Name}, 动作数: {Actions.Count}");
    }

    public void LoadSession(AutoClickSession? session)
    {
        if (session == null) return;

        // 停止当前播放或录制
        if (IsPlaying) StopPlayback();
        if (IsRecording) StopRecording();

        // 清空当前动作
        Actions.Clear();

        // 加载动作
        foreach (var actionData in session.Actions)
        {
            var action = actionData.ToAction();
            Actions.Add(action);
        }

        // 恢复设置
        RepeatCount = session.RepeatCount;
        DefaultDelay = session.DefaultDelay;
        CurrentSession = session;
        
        // 重置脏标记（刚加载的会话是干净的）
        IsDirty = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CurrentSessionName));

        StatusText = $"📂 已加载: {session.Name} ({Actions.Count} 个动作)";
        Log.Debug($"加载会话: {session.Name}, 动作数: {Actions.Count}");
    }

    public void DeleteSession(AutoClickSession? session)
    {
        if (session == null) return;

        // 直接调用 AutoClickManager.Delete，会触发 OnItemRemoved 事件
        AutoClickManager.Instance.Delete(session);

        StatusText = $"🗑️ 已删除: {session.Name}";
        Log.Debug($"删除会话: {session.Name}");
    }

    public void NewSession()
    {
        // 停止当前播放或录制
        if (IsPlaying) StopPlayback();
        if (IsRecording) StopRecording();

        // 清空当前动作
        Actions.Clear();
        
        // 创建一个新的会话（会触发 OnItemAdded 事件）
        var tempSession = AutoClickManager.Instance.CreateNewSession($"新会话_{DateTime.Now:yyyyMMdd_HHmmss}");
        CurrentSession = tempSession;
        
        // 重置脏标记（新会话是干净的）
        IsDirty = false;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CurrentSessionName));

        StatusText = $"🆕 已创建: {tempSession.Name}";
    }

    #endregion

    #region 手动添加动作（辅助功能）

    [RelayCommand]
    private void AddMouseClickAction()
    {
        var action = new AutoClickAction("MouseClick", $"鼠标左键点击", _defaultDelay)
        {
            MouseButton = MouseButton.Button1
        };
        Actions.Add(action);
        StatusText = "已添加鼠标左键点击";
    }

    [RelayCommand]
    private void AddMouseRightClickAction()
    {
        var action = new AutoClickAction("MouseClick", $"鼠标右键点击", _defaultDelay)
        {
            MouseButton = MouseButton.Button2
        };
        Actions.Add(action);
        StatusText = "已添加鼠标右键点击";
    }

    [RelayCommand]
    private void AddDelayAction()
    {
        var action = new AutoClickAction("Delay", $"延迟 {_defaultDelay}ms", _defaultDelay);
        Actions.Add(action);
        StatusText = "已添加延迟动作";
    }

    /// <summary>
    /// 添加自定义按键动作（需要传入KeyCode）
    /// </summary>
    public void AddCustomKeyAction(KeyCode keyCode)
    {
        var keyName = GetKeyCodeDescription(keyCode);
        var action = new AutoClickAction("KeyPress", $"按键: {keyName}", _defaultDelay)
        {
            KeyCode = keyCode
        };
        Actions.Add(action);
        StatusText = $"已添加按键: {keyName}";
    }

    #endregion

    private async Task ExecuteAction(AutoClickAction action)
    {
        try
        {
            switch (action.ActionType)
            {
                case "MouseClick":
                    if (action.MouseButton.HasValue)
                    {
                        // 如果有坐标信息，先移动鼠标到该位置
                        if (action.MouseX != 0 || action.MouseY != 0)
                        {
                            InputSimulateManager.Instance.SendMouseMove(action.MouseX, action.MouseY);
                            await Task.Delay(50); // 等待移动完成
                        }
                        await InputSimulateManager.Instance.SendMouseClick(action.MouseButton.Value, 50);
                    }
                    break;
                    
                case "MouseDown":
                    if (action.MouseButton.HasValue)
                    {
                        if (action.MouseX != 0 || action.MouseY != 0)
                        {
                            InputSimulateManager.Instance.SendMouseMove(action.MouseX, action.MouseY);
                            await Task.Delay(50);
                        }
                        InputSimulateManager.Instance.SimulateMousePress(action.MouseButton.Value);
                        
                        // 如果有持续时间，等待相应时间
                        if (action.Duration.HasValue && action.Duration.Value > 0)
                        {
                            await Task.Delay(action.Duration.Value, _playbackCts!.Token);
                        }
                    }
                    break;
                    
                case "MouseUp":
                    if (action.MouseButton.HasValue)
                    {
                        InputSimulateManager.Instance.SimulateMouseRelease(action.MouseButton.Value);
                    }
                    break;
                    
                case "MouseMove":
                    if (action.MouseX != 0 || action.MouseY != 0)
                    {
                        InputSimulateManager.Instance.SendMouseMove(action.MouseX, action.MouseY);
                        // MouseMove 也需要等待延迟时间，以保持与录制时相同的节奏
                        if (action.Delay > 0)
                        {
                            var adjustedDelay = (int)(action.Delay / PlaybackSpeed);
                            await Task.Delay(Math.Max(10, adjustedDelay), _playbackCts!.Token);
                        }
                    }
                    break;
                    
                case "MouseWheel":
                    if (action.WheelDelta.HasValue)
                    {
                        InputSimulateManager.Instance.SimulateMouseWheel(action.WheelDelta.Value);
                    }
                    break;
                    
                case "KeyPress":
                    if (action.KeyCode.HasValue)
                    {
                        await InputSimulateManager.Instance.SendKeyPress(action.KeyCode.Value, 50);
                    }
                    break;
                    
                case "KeyDown":
                    if (action.KeyCode.HasValue)
                    {
                        InputSimulateManager.Instance.SimulateKeyPress(action.KeyCode.Value);
                        
                        // 如果有持续时间，等待相应时间
                        if (action.Duration.HasValue && action.Duration.Value > 0)
                        {
                            await Task.Delay(action.Duration.Value, _playbackCts!.Token);
                        }
                    }
                    break;
                    
                case "KeyUp":
                    if (action.KeyCode.HasValue)
                    {
                        InputSimulateManager.Instance.SimulateKeyRelease(action.KeyCode.Value);
                    }
                    break;
                    
                case "Text":
                    if (!string.IsNullOrEmpty(action.Text))
                    {
                        await InputSimulateManager.Instance.SendText(action.Text, 50);
                    }
                    break;
                    
                case "Delay":
                    await Task.Delay(action.Delay);
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error($"执行动作失败: {e.Message}");
        }
    }

    public override void OnDisable()
    {
        base.OnDisable();
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
    }
}


