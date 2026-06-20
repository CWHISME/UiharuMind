using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UiharuMind.Core.AutoClick;
using UiharuMind.Services;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;
using UiharuMind.Views.Windows.AutoClick;
using UiharuMind.Views.Windows.Common;
using Ursa.Controls;

namespace UiharuMind.Views.Windows;

public partial class AutoClickWindow : UiharuWindowBase
{
    private readonly QuickAutoClickViewModel _viewModel;
    private RecordingIndicatorWindow? _indicatorWindow;
    private PlaybackIndicatorWindow? _playbackWindow;
    private bool _isRestoringSessionSelection;

    public AutoClickWindow()
    {
        InitializeComponent();
        _viewModel = App.ViewModel.GetViewModel<QuickAutoClickViewModel>();
        _viewModel.SetRecordingCallbacks(PrepareForRecording, FinishRecording);
        _viewModel.SetPlaybackCallback(UpdatePlaybackIndicator, FinishPlayback);
        _viewModel.OnActionRecorded += UpdateRecordingIndicator;
        DataContext = _viewModel;
    }

    public override bool IsCacheWindow => true;

    protected override void OnPreShow()
    {
        base.OnPreShow();
        _viewModel.StopRecordingCommand.Execute(null);
        _viewModel.StopPlaybackCommand.Execute(null);
        _viewModel.OnDisable();
        _indicatorWindow?.Close();
        _playbackWindow?.Close();
    }

    public void PrepareForRecording()
    {
        UIManager.ShowWindow<RecordingIndicatorWindow>(x =>
        {
            _indicatorWindow = x;
            SafeClose();
        });
    }

    public void FinishRecording()
    {
        _indicatorWindow?.SafeClose();
        _indicatorWindow = null;
        Dispatcher.UIThread.Post(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            UIManager.RefreshMacApplicationActivationPolicy();
            Activate();
        }, DispatcherPriority.ApplicationIdle);
    }

    public void UpdateRecordingIndicator(int count)
    {
        _indicatorWindow?.UpdateActionCount(count);
        _indicatorWindow?.FlashIndicator();
    }

    public void UpdatePlaybackIndicator(int current, int total)
    {
        if (_playbackWindow == null)
        {
            _playbackWindow = new PlaybackIndicatorWindow();
            _playbackWindow.Show();
            Hide();
        }

        _playbackWindow.UpdateProgress(current, total);
        _playbackWindow.FlashIndicator();
    }

    private void FinishPlayback()
    {
        _playbackWindow?.Close();
        _playbackWindow = null;
        Show();
        Activate();
    }

    private async void AddKeyButton_Click(object? sender, RoutedEventArgs e)
    {
        var result = await KeySelectionWindow.ShowSingleKeyDialog(this);
        if (result != null)
        {
            _viewModel.AddCustomKeyAction(result.MainKeyCode);
        }
    }

    private async void ChangeSelectedKeyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedStep == null) return;
        var result = await KeySelectionWindow.ShowSingleKeyDialog(this);
        if (result != null)
        {
            _viewModel.SelectedStep.KeyCode = result.MainKeyCode;
        }
    }

    private async void SessionList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringSessionSelection || e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not AutoClickSession session) return;
        if (session == _viewModel.CurrentSession) return;

        if (_viewModel.HasUnsavedChanges)
        {
            var result = await App.MessageService.ShowConfirmMessageBox(
                LocalizationManager.Instance.GetString("AutoClickDiscardUnsavedConfirm"),
                this);

            if (result != MessageBoxResult.Yes)
            {
                if (sender is ListBox listBox)
                {
                    _isRestoringSessionSelection = true;
                    listBox.SelectedItem = _viewModel.CurrentSession;
                    _isRestoringSessionSelection = false;
                }

                return;
            }
        }

        _viewModel.LoadSession(session);
    }

    private async void RenameSessionMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (((Control)e.Source!).DataContext is not AutoClickSession session) return;
        var result = await UIManager.ShowStringEditWindow(session.Name, this);
        if (!string.IsNullOrWhiteSpace(result))
        {
            _viewModel.RenameSession(session, result);
        }
    }

    private async void DeleteSessionMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (((Control)e.Source!).DataContext is AutoClickSession session)
        {
            await ConfirmAndDeleteSession(session);
        }
    }

    private async void DeleteSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: AutoClickSession session })
        {
            await ConfirmAndDeleteSession(session);
        }
    }

    private async System.Threading.Tasks.Task ConfirmAndDeleteSession(AutoClickSession session)
    {
        if (session.StepCount == 0)
        {
            _viewModel.DeleteSession(session);
            return;
        }

        var message = string.Format(LocalizationManager.Instance.GetString("AutoClickDeleteSessionConfirm"),
            session.Name, session.StepCount);
        var result = await App.MessageService.ShowConfirmMessageBox(message, this);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.DeleteSession(session);
        }
    }
}
