using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Shared.Shell;

public partial class SearchViewModel : ViewModelBase
{
    private const int MaxResults = 200;
    private const int MaxHistoryItems = 10;

    private readonly SearchService _searchService;
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _debounceTimer;
    private bool _isInitialized;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isContentMode;
    [ObservableProperty] private bool _isRegexMode;
    [ObservableProperty] private bool _isCaseSensitive;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusMessage = LocalizationManager.Instance.GetString("FileSearchStatusReady");
    [ObservableProperty] private string _currentDirectory = string.Empty;
    [ObservableProperty] private bool _autoSearch = true;
    [ObservableProperty] private bool _hasNoResults = true;

    public ObservableCollection<SearchItem> Results { get; } = new();
    public ObservableCollection<string> HistoryDirectories { get; } = new();

    public SearchViewModel()
    {
    }

    public SearchViewModel(SearchService searchService)
    {
        _searchService = searchService;
        LoadHistory();
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (!_isInitialized) return;
        if (AutoSearch) DebounceSearch();
    }

    partial void OnAutoSearchChanged(bool value)
    {
        if (!_isInitialized) return;
        if (value && !string.IsNullOrWhiteSpace(SearchQuery))
            DebounceSearch();
    }

    partial void OnIsContentModeChanged(bool value) => TriggerSearchIfAuto();
    partial void OnIsRegexModeChanged(bool value) => TriggerSearchIfAuto();
    partial void OnIsCaseSensitiveChanged(bool value) => TriggerSearchIfAuto();

    private void TriggerSearchIfAuto()
    {
        if (_isInitialized && AutoSearch && !string.IsNullOrWhiteSpace(SearchQuery))
            DebounceSearch();
    }

    private void DebounceSearch()
    {
        _debounceTimer?.Stop();
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick += OnDebounceTick;
        _debounceTimer.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        _ = SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            Results.Clear();
            HasNoResults = true;
            StatusMessage = LocalizationManager.Instance.GetString("FileSearchStatusNeedInput");
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        IsSearching = true;
        HasNoResults = false;
        StatusMessage = LocalizationManager.Instance.GetString("FileSearchStatusSearching");
        Results.Clear();

        try
        {
            var results = await _searchService.SearchAsync(
                SearchQuery,
                IsContentMode,
                IsRegexMode,
                IsCaseSensitive,
                _searchCts.Token);

            var display = results.Take(MaxResults).ToList();
            foreach (var item in display)
            {
                Results.Add(item);
            }

            var count = results.Count;
            HasNoResults = count == 0;
            StatusMessage = count > MaxResults
                ? string.Format(LocalizationManager.Instance.GetString("FileSearchStatusResultFormat"), count) + $" (top {MaxResults})"
                : string.Format(LocalizationManager.Instance.GetString("FileSearchStatusResultFormat"), count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = LocalizationManager.Instance.GetString("FileSearchStatusCancelled");
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task SelectDirectoryAsync()
    {
        var path = await App.FilesService.OpenSelectFolderAsync(CurrentDirectory, UIManager.GetFocusWindow());
        if (!string.IsNullOrEmpty(path)) ApplyDirectory(path);
    }

    [RelayCommand]
    private void SetDirectory(string path)
    {
        if (!string.IsNullOrEmpty(path)) ApplyDirectory(path);
    }

    private void ApplyDirectory(string path)
    {
        _searchService.AddHistory(path);
        UpdateHistory(path);
        if (AutoSearch && !string.IsNullOrWhiteSpace(SearchQuery))
            DebounceSearch();
    }

    private void UpdateHistory(string path)
    {
        var existingIndex = HistoryDirectories.IndexOf(path);
        if (existingIndex == 0) return; // 已经是第一项

        if (existingIndex > 0)
        {
            // 移动到首位
            HistoryDirectories.Move(existingIndex, 0);
        }
        else
        {
            // 新项插入首位
            HistoryDirectories.Insert(0, path);
        }

        CurrentDirectory = path;
        while (HistoryDirectories.Count > MaxHistoryItems)
            HistoryDirectories.RemoveAt(HistoryDirectories.Count - 1);
    }

    [RelayCommand]
    private void OpenCurrentDirectory()
    {
        if (!string.IsNullOrEmpty(CurrentDirectory))
            App.FilesService.OpenFolder(CurrentDirectory);
    }

    public void Initialize()
    {
        _isInitialized = true;
    }

    public void LoadHistory()
    {
        HistoryDirectories.Clear();
        foreach (var dir in _searchService.GetHistory())
        {
            HistoryDirectories.Add(dir);
        }

        CurrentDirectory = HistoryDirectories.FirstOrDefault() ?? "";
    }
}