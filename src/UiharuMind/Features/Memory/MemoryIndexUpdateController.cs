using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Core.AI.Memory;

namespace UiharuMind.Features.Memory;

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
        ActionText = Loc.Text(LangKey.MemoryIndexUpdate);
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
        ProgressStageText = Loc.Text(LangKey.MemoryIndexPreparingShort);
        ProgressDetailText = "";
        ActionText = Loc.Text(LangKey.MemoryIndexStop);

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
                    Loc.Text(LangKey.MemoryIndexUpdateSuccess), severity: MessageSeverity.Success);
            }
            else if (result.Cancelled)
            {
                _messageService.ShowNotification(Loc.Text(LangKey.MemoryIndexCancelledOldIndexKept));
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
            ActionText = Loc.Text(LangKey.MemoryIndexUpdate);
            _cancellation?.Dispose();
            _cancellation = null;
            _updateTask = null;
            OnPropertyChanged(nameof(HasBackgroundWork));
        }
    }

    private void ApplyProgress(MemoryIndexProgress progress)
    {
        ProgressValue = progress.Percentage * 100;
        ProgressStageText = Loc.Text(progress.Stage switch
        {
            MemoryIndexStage.ReadingSources => LangKey.MemoryIndexStageReadingSources,
            MemoryIndexStage.SplittingText => LangKey.MemoryIndexStageSplittingText,
            MemoryIndexStage.GeneratingEmbeddings => LangKey.MemoryIndexStageGeneratingEmbeddings,
            MemoryIndexStage.WritingDatabase => LangKey.MemoryIndexStageWritingDatabase,
            MemoryIndexStage.Completed => LangKey.MemoryIndexStageCompleted,
            _ => LangKey.MemoryIndexStagePreparing,
        });
        ProgressDetailText = string.Format(Loc.Text(LangKey.MemoryIndexProgressFormat),
            progress.ProcessedSources, progress.TotalSources,
            progress.CurrentChunk, progress.TotalChunks,
            progress.CurrentSource,
            Math.Max(0, progress.ProcessedSources - progress.FailedSources),
            progress.FailedSources);
    }

    private static string BuildFailureText(MemoryIndexUpdateResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine(Loc.Text(LangKey.MemoryIndexUpdateFailed));
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
}
