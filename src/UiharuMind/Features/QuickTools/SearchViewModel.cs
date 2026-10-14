using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Features.QuickTools;

/// <summary>面包屑一级：可点击回退的父目录（末段当前目录走可编辑路径框，不入这里）</summary>
public sealed record DirSegment(string Label, string FullPath);

public partial class SearchViewModel : ViewModelBase
{
    private const int MaxResults = 200;
    private const int MaxHistoryItems = 10;
    private const int MaxLayerItems = 4000; //列一层封顶，防巨型目录刷爆虚拟化列表

    private readonly SearchService _searchService = null!; // 由带参构造函数（DI）赋值
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _debounceTimer;
    private bool _isInitialized;

    // 单 flight 串行:飞行中途的新请求只记一个"重跑"标志,落地后用最新条件再跑一遍,
    // 旧结果永远不会反超盖掉新结果。引擎停不下来,取消只是不再等它;防扫描堆积靠 SimpleGrepper 的闸。
    private bool _searchInFlight;
    private bool _searchRerunRequested;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isContentMode;
    [ObservableProperty] private bool _isRegexMode;
    [ObservableProperty] private bool _isCaseSensitive;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusMessage = Loc.Text(LangKey.FileSearchStatusReady);
    [ObservableProperty] private string _currentDirectory = string.Empty;
    [ObservableProperty] private bool _autoSearch = true;
    [ObservableProperty] private bool _hasNoResults = true;

    public ObservableCollection<SearchItem> Results { get; } = new();
    public ObservableCollection<string> HistoryDirectories { get; } = new();

    // 面包屑：当前目录的<b>父目录链</b>（根..父，不含自身）。末段当前目录走可编辑路径框，不入这里。
    // 点任一父级回退到那一段（NavigateToSegment），点结果里的目录条目下钻（EnterDirectory）。
    public ObservableCollection<DirSegment> Breadcrumb { get; } = new();

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
        // 飞行中有新请求:只标记重跑,不另起一轮。
        // 查询已清空的情况也走这条:本轮落地后重跑会走到下面的清空分支,最终状态是对的。
        if (_searchInFlight)
        {
            _searchRerunRequested = true;
            _searchCts?.Cancel(); // 不再等旧扫描,本轮立刻落地、用新条件重跑
            return;
        }

        _searchInFlight = true;
        IsSearching = true;
        try
        {
            do
            {
                _searchRerunRequested = false;
                await RunSearchOnceAsync();
            } while (_searchRerunRequested);
        }
        finally
        {
            _searchInFlight = false;
            IsSearching = false;
        }
    }

    private async Task RunSearchOnceAsync()
    {
        // 浏览器化语义（圣娅钉的）：无 query = 列当前目录一层（浏览态）；
        // 有 query = 以当前目录为根向下递归搜（保住全树能力，只是根随面包屑走）。
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await RefreshLayerAsync();
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var searchCts = _searchCts; // 身份标记:落地时不是这一轮就丢掉,不让旧结果反超

        HasNoResults = false;
        StatusMessage = Loc.Text(LangKey.FileSearchStatusSearching);
        Results.Clear();

        try
        {
            // root=CurrentDirectory：面包屑即搜索根。递归搜 **/*query* 从当前目录向下
            SearchOutcome outcome = await _searchService.SearchAsync(
                SearchQuery,
                IsContentMode,
                IsRegexMode,
                IsCaseSensitive,
                searchCts.Token,
                root: CurrentDirectory);

            // 落地时已有更新的请求在排队:本轮结果已过期,直接丢掉,重跑会给出最新结果
            if (_searchRerunRequested || !ReferenceEquals(_searchCts, searchCts)) return;

            // 失败要说出来。从前失败会伪装成一条搜索结果(目录不存在的提示串当文件名显示),
            // 或者被静默吞成"0 结果"——两种都让用户分不清"没搜到"和"没搜成"
            string? failureText = DescribeFailure(outcome);
            if (failureText != null)
            {
                HasNoResults = true;
                StatusMessage = failureText;
                return;
            }

            var display = outcome.Items.Take(MaxResults).ToList();
            foreach (var item in display)
            {
                Results.Add(item);
            }

            var count = outcome.Items.Count;
            HasNoResults = count == 0;
            StatusMessage = count > MaxResults
                ? string.Format(Loc.Text(LangKey.FileSearchStatusResultFormat), count) + $" (top {MaxResults})"
                : string.Format(Loc.Text(LangKey.FileSearchStatusResultFormat), count);
        }
        catch (OperationCanceledException)
        {
            // 被新一轮取代的取消不写状态,重跑会自己写"搜索中";只有顶层取消才算一次取消
            if (!_searchRerunRequested)
                StatusMessage = Loc.Text(LangKey.FileSearchStatusCancelled);
        }
    }

    // 浏览态：列当前目录一层。没有当前目录（首开无历史）就不列，提示就绪。
    private async Task RefreshLayerAsync()
    {
        if (string.IsNullOrEmpty(CurrentDirectory))
        {
            Results.Clear();
            HasNoResults = true;
            StatusMessage = Loc.Text(LangKey.FileSearchStatusReady);
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var searchCts = _searchCts;

        HasNoResults = false;
        Results.Clear();

        try
        {
            LayerListing layer = await _searchService.ListLayerAsync(CurrentDirectory, searchCts.Token);

            if (_searchRerunRequested || !ReferenceEquals(_searchCts, searchCts)) return;

            if (layer.ErrorDetail != null)
            {
                HasNoResults = true;
                StatusMessage = string.Format(Loc.Text(LangKey.FileSearchStatusFailed), layer.ErrorDetail);
                return;
            }

            var display = layer.Items.Take(MaxLayerItems).ToList();
            foreach (var item in display)
            {
                Results.Add(item);
            }

            HasNoResults = display.Count == 0;
            StatusMessage = string.Format(Loc.Text(LangKey.FileSearchStatusLayer), layer.Items.Count);
        }
        catch (OperationCanceledException)
        {
            if (!_searchRerunRequested)
                StatusMessage = Loc.Text(LangKey.FileSearchStatusCancelled);
        }
    }

    /// <summary>
    /// 把失败原因渲染成状态行文案。同一份结构化事实，模型那侧由
    /// <c>SearchFailureRenderer</c> 渲染成给模型看的英文说明，这里渲染成给用户看的本地化文案。
    /// </summary>
    /// <param name="outcome">一次搜索的结果</param>
    /// <returns>失败文案；没失败则为 null</returns>
    private static string? DescribeFailure(SearchOutcome outcome)
    {
        if (outcome.ErrorDetail != null)
        {
            return string.Format(Loc.Text(LangKey.FileSearchStatusFailed),
                outcome.ErrorDetail);
        }

        if (outcome.Failure == null) return null;

        return outcome.Failure.Kind switch
        {
            ESearchFailureKind.PathNotFound => string.Format(
                Loc.Text(LangKey.FileSearchStatusDirectoryNotFound),
                outcome.Failure.ResolvedDirectory),
            ESearchFailureKind.InvalidGlobPattern => string.Format(
                Loc.Text(LangKey.FileSearchStatusInvalidPattern),
                outcome.Failure.Detail),
            // 界面侧的 glob 会自动包成 **/*query*,所以"没有通配符"这一种在这里不会发生;
            // 真落到这里就按通用失败报，总比静默好
            _ => string.Format(Loc.Text(LangKey.FileSearchStatusFailed),
                outcome.Failure.Detail),
        };
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
        else
            _ = RefreshLayerAsync(); // 没 query 时切目录刷新面包屑与层列表
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

    /// <summary>点结果里的目录条目下钻一层（浏览器化的「进」）</summary>
    [RelayCommand]
    private void EnterDirectory(SearchItem item)
    {
        if (!item.IsDirectory) return;
        string full = Path.GetFullPath(Path.Combine(CurrentDirectory, item.Path));
        ApplyDirectory(full);
    }

    /// <summary>点面包屑任一段回退到那个父目录</summary>
    [RelayCommand]
    private void NavigateToSegment(DirSegment segment)
    {
        if (segment != null) ApplyDirectory(segment.FullPath);
    }

    public void Initialize()
    {
        _isInitialized = true;
        // 首开无 query，直接列当前目录一层（有历史目录的话）；没有则提示就绪
        _ = RefreshLayerAsync();
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

    // 任何途径改当前目录（首开/下钻/回退/切根）都重建面包屑，收在这一个 hook
    partial void OnCurrentDirectoryChanged(string value) => RebuildBreadcrumb();

    // 面包屑 = 当前目录的父目录链（根..父，不含自身）。末段当前目录走可编辑路径框，不入这里。
    private void RebuildBreadcrumb()
    {
        Breadcrumb.Clear();
        if (string.IsNullOrEmpty(CurrentDirectory)) return;

        // 从当前目录的<b>父级</b>起逐级上溯到根，根在前（Insert(0)），
        // 当前目录自身留给末段路径框，避免与路径框重复显示同一段
        string? parent = Path.GetDirectoryName(Path.GetFullPath(CurrentDirectory));
        while (!string.IsNullOrEmpty(parent))
        {
            string trimmed = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            // 到根就停：unix 根（/ 砍空）、windows 驱动器根（裸 C:）都不该再往上
            if (name.Length == 0 || (name.Length == 2 && name[1] == ':')) break;
            Breadcrumb.Insert(0, new DirSegment(name, parent));
            parent = Path.GetDirectoryName(parent);
        }
    }
}