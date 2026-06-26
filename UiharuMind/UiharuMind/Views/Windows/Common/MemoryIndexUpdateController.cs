using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;

namespace UiharuMind.Views.Windows.Common;

/// <summary>
/// 统一管理记忆索引的启动、取消、进度与用户提示，避免各窗口重复维护任务状态。
/// </summary>
public partial class MemoryIndexUpdateController : ObservableObject, IDisposable
{
    private readonly IMessageService _messageService;
    private MemoryData _memory;
    private CancellationTokenSource? _cancellation;
    private Task? _updateTask;

    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressStageText = "";
    [ObservableProperty] private string _progressDetailText = "";
    [ObservableProperty] private string _actionText = "";
    [ObservableProperty] private bool _hasFailure;
    [ObservableProperty] private string _failureText = "";

    public bool HasBackgroundWork => _updateTask is { IsCompleted: false };
    public MemoryData Memory => _memory;

    public event Action<MemoryIndexUpdateResult>? Completed;

    public MemoryIndexUpdateController(MemoryData memory, IMessageService messageService)
    {
        _memory = memory;
        _messageService = messageService;
        ActionText = L("MemoryIndexUpdate");
    }

    public void ChangeMemory(MemoryData memory)
    {
        if (IsUpdating)
            throw new InvalidOperationException("Cannot change memory while its index is updating.");
        _memory = memory;
        HasFailure = false;
        FailureText = "";
        ProgressValue = 0;
        ProgressStageText = "";
        ProgressDetailText = "";
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Update()
    {
        if (IsUpdating)
        {
            _cancellation?.Cancel();
            return;
        }

        _cancellation = new CancellationTokenSource();
        IsUpdating = true;
        HasFailure = false;
        FailureText = "";
        ProgressValue = 0;
        ProgressStageText = L("MemoryIndexPreparingShort");
        ProgressDetailText = "";
        ActionText = L("MemoryIndexStop");

        var progress = new Progress<MemoryIndexProgress>(ApplyProgress);
        _updateTask = RunUpdateAsync(progress, _cancellation.Token);
        OnPropertyChanged(nameof(HasBackgroundWork));
        await _updateTask;
    }

    public async Task CancelAndWaitAsync()
    {
        _cancellation?.Cancel();
        if (_updateTask == null) return;
        try
        {
            await _updateTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunUpdateAsync(
        IProgress<MemoryIndexProgress> progress,
        CancellationToken cancellationToken)
    {
        MemoryIndexUpdateResult result;
        try
        {
            result = await _memory.UpdateIndexAsync(progress, cancellationToken);
            if (result.Succeeded)
            {
                _messageService.ShowNotification(
                    L("MemoryIndexUpdateSuccess"), severity: MessageSeverity.Success);
            }
            else if (result.Cancelled)
            {
                _messageService.ShowNotification(L("MemoryIndexCancelledOldIndexKept"));
            }
            else
            {
                HasFailure = true;
                FailureText = BuildFailureText(result);
                await _messageService.ShowWarningAsync(FailureText, cancellationToken: cancellationToken);
            }

            Completed?.Invoke(result);
        }
        finally
        {
            IsUpdating = false;
            ActionText = L("MemoryIndexUpdate");
            _cancellation?.Dispose();
            _cancellation = null;
            _updateTask = null;
            OnPropertyChanged(nameof(HasBackgroundWork));
        }
    }

    private void ApplyProgress(MemoryIndexProgress progress)
    {
        ProgressValue = progress.Percentage * 100;
        ProgressStageText = L("MemoryIndexStage" + progress.Stage);
        ProgressDetailText = string.Format(L("MemoryIndexProgressFormat"),
            progress.ProcessedSources, progress.TotalSources,
            progress.CurrentChunk, progress.TotalChunks,
            progress.CurrentSource,
            Math.Max(0, progress.ProcessedSources - progress.FailedSources),
            progress.FailedSources);
    }

    private static string BuildFailureText(MemoryIndexUpdateResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine(L("MemoryIndexUpdateFailed"));
        foreach (MemoryIndexSourceFailure failure in result.Failures)
        {
            builder.Append(failure.SourceName)
                .Append(": ")
                .AppendLine(MemoryIndexUiText.GetSourceErrorText(
                    failure.ErrorCode, failure.ErrorDetail));
        }

        if (result.Failures.Count == 0 && !string.IsNullOrWhiteSpace(result.Error))
            builder.AppendLine(MemoryIndexUiText.GetIndexErrorText(result.Error));
        return builder.ToString().Trim();
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }

    private static string L(string key) =>
        Lang.ResourceManager.GetString(key, LocalizationManager.Instance.CurrentCulture) ?? key;
}
