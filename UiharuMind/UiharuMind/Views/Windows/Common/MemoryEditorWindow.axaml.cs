using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;

namespace UiharuMind.Views.Windows.Common;

public partial class MemoryEditorWindow : Window
{
    private bool _closeAfterCancellation;

    public MemoryEditorWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeAfterCancellation && DataContext is MemoryEditorWindowModel { HasBackgroundWork: true } model)
        {
            e.Cancel = true;
            await model.CancelAndWaitAsync();
            _closeAfterCancellation = true;
            Close();
            return;
        }

        (DataContext as MemoryEditorWindowModel)?.OnClose();
        base.OnClosing(e);
    }
}

public partial class MemoryEditorWindowModel : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly MemoryData _memoryData;
    private readonly Action? _onClose;
    private CancellationTokenSource? _importCancellation;
    private Task? _importTask;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Task _fileDetailsTask;

    public ObservableCollection<MemoryTextSource> TextSources { get; }
    public ObservableCollection<MemoryFileSourceViewData> Files { get; } = [];
    public MemoryIndexUpdateController IndexUpdater { get; }

    [ObservableProperty] private string _indexStatusText = "";
    [ObservableProperty] private string _indexStatusDetailText = "";
    [ObservableProperty] private string _indexStatusKey = "Dirty";
    [ObservableProperty] private string _lastIndexedText = "";
    [ObservableProperty] private string _sourceCountText = "";
    [ObservableProperty] private string _addFileActionText = "";
    [ObservableProperty] private string _failureText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanUpdateIndex))]
    private bool _isFileImporting;

    [ObservableProperty] private bool _hasFailure;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryDescriptionDisplay))]
    [NotifyPropertyChangedFor(nameof(CanSaveDescription))]
    private string _memoryDescription = "";

    public bool IsBusy => IndexUpdater.IsUpdating || IsFileImporting;
    public bool HasBackgroundWork => IndexUpdater.HasBackgroundWork ||
                                     IsFileImporting ||
                                     !_fileDetailsTask.IsCompleted;
    public bool CanUpdateIndex => !IsFileImporting;
    public bool CanImportFiles => !IndexUpdater.IsUpdating;
    public bool HasTextSources => TextSources.Count > 0;
    public bool HasFiles => Files.Count > 0;
    public string MemoryName => _memoryData.Name;
    public string MemoryDescriptionDisplay => string.IsNullOrWhiteSpace(MemoryDescription)
        ? L("MemoryDescriptionFallback")
        : MemoryDescription;
    public bool CanSaveDescription =>
        !string.Equals(MemoryDescription.Trim(), _memoryData.Description, StringComparison.Ordinal);

    public MemoryEditorWindowModel()
        : this(new MemoryData(), App.Services.GetRequiredService<IMessageService>())
    {
    }

    public MemoryEditorWindowModel(
        MemoryData memoryData,
        Action? onClose = null)
        : this(memoryData, App.Services.GetRequiredService<IMessageService>(), onClose)
    {
    }

    public MemoryEditorWindowModel(
        MemoryData memoryData,
        IMessageService messageService,
        Action? onClose = null)
    {
        _memoryData = memoryData;
        _messageService = messageService;
        _onClose = onClose;
        IndexUpdater = new MemoryIndexUpdateController(memoryData, messageService);
        IndexUpdater.PropertyChanged += OnIndexUpdaterPropertyChanged;
        IndexUpdater.Completed += OnIndexUpdateCompleted;
        MemoryDescription = memoryData.Description;
        TextSources = new ObservableCollection<MemoryTextSource>(memoryData.TextSources);
        TextSources.CollectionChanged += OnSourcesCollectionChanged;
        Files.CollectionChanged += OnSourcesCollectionChanged;
        _memoryData.StateChanged += OnMemoryStateChanged;
        RefreshStatus();
        _fileDetailsTask = LoadFileDetailsAsync(_lifetimeCancellation.Token);
    }

    public async Task CancelAndWaitAsync()
    {
        _importCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        await IndexUpdater.CancelAndWaitAsync();

        if (_importTask != null)
        {
            try
            {
                await _importTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await _fileDetailsTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task AddText()
    {
        MemoryTextSource? source =
            await UIManager.ShowMemoryTextSourceEditWindow(UIManager.GetFoucusWindow());
        if (source == null) return;

        TextSources.Add(source);
        _memoryData.TextSources.Add(source);
        _memoryData.Save();
        RefreshStatus();
    }

    [RelayCommand]
    private void SaveDescription()
    {
        if (!CanSaveDescription) return;
        _memoryData.Description = MemoryDescription.Trim();
        _memoryData.SaveMetadata();
        OnPropertyChanged(nameof(MemoryDescriptionDisplay));
        OnPropertyChanged(nameof(CanSaveDescription));
        _messageService.ShowNotification(
            L("MemoryDescriptionSaved"), severity: MessageSeverity.Success);
    }

    [RelayCommand]
    private async Task EditText(MemoryTextSource source)
    {
        MemoryTextSource? edited =
            await UIManager.ShowMemoryTextSourceEditWindow(UIManager.GetFoucusWindow(), source);
        if (edited == null) return;

        source.Title = edited.Title;
        source.Content = edited.Content;
        int index = TextSources.IndexOf(source);
        if (index >= 0)
        {
            TextSources[index] = source;
            OnPropertyChanged(nameof(TextSources));
        }

        _memoryData.Save();
        RefreshStatus();
    }

    [RelayCommand]
    private async Task DeleteText(MemoryTextSource source)
    {
        if (!await _messageService.ConfirmAsync(L("CommonDeleteConfirmTips"))) return;
        TextSources.Remove(source);
        _memoryData.TextSources.Remove(source);
        _memoryData.Save();
        RefreshStatus();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AddFile()
    {
        if (IsFileImporting)
        {
            _importCancellation?.Cancel();
            return;
        }
        if (IndexUpdater.IsUpdating) return;

        IReadOnlyList<IStorageFile> files = await App.FilesService.SelectFileAsync(
            UIManager.GetFoucusWindow(), null,
            L("MemorySelectTextFilesTitle"), L("MemoryTextFileFilter"), "*");
        if (files.Count == 0) return;

        _importCancellation = new CancellationTokenSource();
        IsFileImporting = true;
        HasFailure = false;
        FailureText = "";
        IndexUpdater.ProgressValue = 0;
        IndexUpdater.ProgressStageText = L("MemoryImportValidating");
        IndexUpdater.ProgressDetailText = "";
        RefreshStatus();

        _importTask = ImportFilesAsync(files, _importCancellation.Token);
        await _importTask;
    }

    private async Task ImportFilesAsync(
        IReadOnlyList<IStorageFile> files, CancellationToken cancellationToken)
    {
        List<string> errors = [];
        List<(string Path, MemorySourceReadResult Result)> accepted = [];
        try
        {
            for (int index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? path = files[index].TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(path) || _memoryData.FilePaths.Contains(path)) continue;

                IndexUpdater.ProgressStageText = L("MemoryImportValidating");
                IndexUpdater.ProgressDetailText = string.Format(L("MemoryImportProgressFormat"),
                    index + 1, files.Count, Path.GetFileName(path),
                    accepted.Count, errors.Count);
                IndexUpdater.ProgressValue = (index + 1d) / files.Count * 100;

                MemorySourceReadResult result =
                    await _memoryData.ValidateTextFileAsync(path, cancellationToken);
                if (!result.Success)
                {
                    errors.Add($"{Path.GetFileName(path)}: {GetSourceErrorText(result.ErrorCode, result.ErrorDetail)}");
                    continue;
                }

                accepted.Add((path, result));
            }

            foreach ((string path, MemorySourceReadResult result) in accepted)
            {
                _memoryData.FilePaths.Add(path);
                Files.Add(MemoryFileSourceViewData.From(path, result));
            }

            if (accepted.Count > 0) _memoryData.Save();
            if (errors.Count > 0)
            {
                HasFailure = true;
                FailureText = string.Join(Environment.NewLine, errors);
                await _messageService.ShowWarningAsync(FailureText);
            }
            else
            {
                _messageService.ShowNotification(string.Format(
                    L("MemoryImportCompleted"), accepted.Count), severity: MessageSeverity.Success);
            }
        }
        catch (OperationCanceledException)
        {
            _messageService.ShowNotification(L("MemoryImportCancelled"));
        }
        finally
        {
            IsFileImporting = false;
            _importCancellation?.Dispose();
            _importCancellation = null;
            RefreshStatus();
        }
    }

    [RelayCommand]
    private void OpenFileFolder(MemoryFileSourceViewData file)
    {
        App.FilesService.OpenFolder(Path.GetDirectoryName(file.Path) ?? file.Path);
    }

    [RelayCommand]
    private async Task DeleteFile(MemoryFileSourceViewData file)
    {
        if (!await _messageService.ConfirmAsync(L("CommonDeleteConfirmTips"))) return;
        Files.Remove(file);
        _memoryData.FilePaths.Remove(file.Path);
        _memoryData.Save();
        RefreshStatus();
    }

    public void OnClose()
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        IndexUpdater.PropertyChanged -= OnIndexUpdaterPropertyChanged;
        IndexUpdater.Completed -= OnIndexUpdateCompleted;
        IndexUpdater.Dispose();
        TextSources.CollectionChanged -= OnSourcesCollectionChanged;
        Files.CollectionChanged -= OnSourcesCollectionChanged;
        _memoryData.StateChanged -= OnMemoryStateChanged;
        _onClose?.Invoke();
    }

    private async Task LoadFileDetailsAsync(CancellationToken cancellationToken)
    {
        foreach (string path in _memoryData.FilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MemorySourceReadResult result = await _memoryData.ValidateTextFileAsync(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() => Files.Add(MemoryFileSourceViewData.From(path, result)));
        }
    }

    private void OnMemoryStateChanged()
    {
        Dispatcher.UIThread.Post(RefreshStatus);
    }

    private void OnSourcesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasTextSources));
        OnPropertyChanged(nameof(HasFiles));
    }

    private void OnIndexUpdaterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoryIndexUpdateController.IsUpdating))
        {
            if (IndexUpdater.IsUpdating)
            {
                HasFailure = false;
                FailureText = "";
            }

            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanImportFiles));
            OnPropertyChanged(nameof(HasBackgroundWork));
            RefreshStatus();
        }
        else if (e.PropertyName == nameof(MemoryIndexUpdateController.HasFailure))
        {
            HasFailure = IndexUpdater.HasFailure;
            FailureText = IndexUpdater.FailureText;
        }
    }

    private void OnIndexUpdateCompleted(MemoryIndexUpdateResult result)
    {
        HasFailure = IndexUpdater.HasFailure;
        FailureText = IndexUpdater.FailureText;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        IndexStatusText = IndexUpdater.IsUpdating
            ? L("MemoryIndexUpdating")
            : !string.IsNullOrWhiteSpace(_memoryData.LastIndexError)
                ? L("MemoryIndexHasError")
                : _memoryData.IndexDirty
                    ? L("MemoryIndexPendingShort")
                : _memoryData.LastIndexedAt == null
                    ? L("MemoryIndexNotBuiltShort")
                    : L("MemoryIndexReady");
        IndexStatusDetailText = IndexUpdater.IsUpdating
            ? L("MemoryIndexUpdatingDetail")
            : !string.IsNullOrWhiteSpace(_memoryData.LastIndexError)
                ? MemoryIndexUiText.GetIndexErrorText(_memoryData.LastIndexError)
                : _memoryData.IndexDirty
                    ? L("MemoryIndexNeedUpdate")
                    : _memoryData.LastIndexedAt == null
                        ? L("MemoryIndexNotBuilt")
                        : L("MemoryIndexReadyDetail");
        IndexStatusKey = IndexUpdater.IsUpdating
            ? "Progress"
            : !string.IsNullOrWhiteSpace(_memoryData.LastIndexError)
                ? "Error"
                : _memoryData.IndexDirty || _memoryData.LastIndexedAt == null
                    ? "Dirty"
                    : "Ready";

        LastIndexedText = _memoryData.LastIndexedAt == null
            ? L("MemoryIndexNeverUpdated")
            : L("MemoryIndexLastIndexed") +
              _memoryData.LastIndexedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

        SourceCountText = string.Format(L("MemorySourceCountFormat"),
            _memoryData.TextSources.Count, _memoryData.FilePaths.Count);
        AddFileActionText = IsFileImporting ? L("MemoryImportStop") : L("MemoryAddTextFile");

        if (!IndexUpdater.IsUpdating && !string.IsNullOrWhiteSpace(_memoryData.LastIndexError) && !HasFailure)
        {
            HasFailure = true;
            FailureText = GetIndexErrorText(_memoryData.LastIndexError);
        }
    }

    private static string GetSourceErrorText(string errorCode, string detail)
    {
        return MemoryIndexUiText.GetSourceErrorText(errorCode, detail);
    }

    private static string GetIndexErrorText(string error)
    {
        return MemoryIndexUiText.GetIndexErrorText(error);
    }

    private static string L(string key) =>
        Lang.ResourceManager.GetString(key, LocalizationManager.Instance.CurrentCulture) ?? key;
}

public sealed class MemoryFileSourceViewData
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string EncodingText { get; init; } = "";
    public string ErrorText { get; init; } = "";
    public bool IsValid { get; init; }

    public static MemoryFileSourceViewData From(string path, MemorySourceReadResult result)
    {
        long size = File.Exists(path) ? new FileInfo(path).Length : 0;
        string sizeText = size >= 1024 * 1024
            ? $"{size / 1024d / 1024d:0.##} MB"
            : $"{Math.Max(1, size / 1024d):0.##} KB";

        return new MemoryFileSourceViewData
        {
            Path = path,
            Name = System.IO.Path.GetFileName(path),
            SizeText = sizeText,
            EncodingText = result.Document?.EncodingName ?? "",
            IsValid = result.Success,
            ErrorText = result.Success
                ? ""
                : Lang.ResourceManager.GetString(result.ErrorCode,
                    LocalizationManager.Instance.CurrentCulture) ?? result.ErrorCode
        };
    }
}
