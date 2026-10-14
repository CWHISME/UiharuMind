using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Features.QuickTools;

/// <summary>面包屑一级：从根到当前逐级可点击，末级是当前目录（只展示不跳转）</summary>
public sealed record DirSegment(string Label, string FullPath, bool IsCurrent);

/// <summary>侧栏一条：收藏或最近的目录。展示与工作区选择器的最近列表同式（名 + 父路径）</summary>
public sealed record SidebarEntry(string Path)
{
    public string Name
    {
        get
        {
            string trimmed = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string name = System.IO.Path.GetFileName(trimmed);
            return name.Length == 0 ? Path : name; // 根（/ 或 C:\）全写才认得出来
        }
    }

    public string Parent
    {
        get
        {
            try
            {
                return System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}

public partial class SearchViewModel : ViewModelBase
{
    private const int MaxResults = 200;
    private const int MaxHistoryItems = 10;
    private const int MaxLayerItems = 4000; //列一层封顶，防巨型目录刷爆虚拟化列表

    private readonly SearchService _searchService = null!; // 由带参构造函数（DI）赋值
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _layerCts;
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

    // 左栏浏览与右栏搜索各写各的集合：从前共用 Results，
    // 有 query 切浏览态会把搜索结果洗掉、切回又要重搜一遍
    public ObservableCollection<SearchItem> Results { get; } = new();
    public ObservableCollection<SearchItem> BrowseItems { get; } = new();
    [ObservableProperty] private string _browseStatusMessage = Loc.Text(LangKey.FileSearchStatusReady);
    [ObservableProperty] private bool _hasNoBrowseResults = true;

    // Finder 式单列：有 query 主列显示搜索结果，否则显示浏览层
    [ObservableProperty] private bool _hasActiveQuery;

    // 侧栏只留收藏。最近收进口径条的下拉（Flyout），不再占一栏：
    // ComboBox 吃 HistoryDirectories（字符串），侧栏与下拉吃条目，
    // 三边在 UpdateHistory/LoadHistory 里同批同步
    public ObservableCollection<SidebarEntry> FavoriteEntries { get; } = new();
    public ObservableCollection<SidebarEntry> RecentEntries { get; } = new();
    [ObservableProperty] private SidebarEntry? _selectedFavorite;
    [ObservableProperty] private bool _isCurrentFavorite;
    [ObservableProperty] private bool _hasNoFavorites = true;
    [ObservableProperty] private string _historyJumpText = string.Empty;

    /// <summary>星标悬停提示：已收藏显示取消，未收藏显示收藏</summary>
    public string FavoriteTip => IsCurrentFavorite
        ? Loc.Text(LangKey.FileSearchRemoveFavorite)
        : Loc.Text(LangKey.FileSearchAddFavorite);

    /// <summary>星标颜色：已收藏金色，未收藏灰（与角色列表的收藏星同口径）</summary>
    public Color FavoriteColor => IsCurrentFavorite ? Colors.Gold : Colors.Gray;

    partial void OnIsCurrentFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoriteTip));
        OnPropertyChanged(nameof(FavoriteColor));
    }

    public ObservableCollection<string> HistoryDirectories { get; } = new();

    // 面包屑：从根到当前的完整链（含自身，末级标 IsCurrent 只展示）。
    // 点任一父级回退到那一段（NavigateToSegment），点浏览栏的目录条目下钻（EnterDirectory）。
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
        // query 清空 = 回浏览态：右栏收起，不留过期结果
        if (string.IsNullOrWhiteSpace(value))
        {
            _searchCts?.Cancel();
            Results.Clear();
            HasNoResults = true;
            HasActiveQuery = false;
            StatusMessage = Loc.Text(LangKey.FileSearchStatusNeedInput);
            return;
        }
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
        // Finder 式单列：无 query 右栏（主列）回到浏览提示，浏览层常驻刷新（另跑，不抢这里的 CTS）
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            _searchCts?.Cancel();
            Results.Clear();
            HasNoResults = true;
            HasActiveQuery = false;
            StatusMessage = Loc.Text(LangKey.FileSearchStatusNeedInput);
            return;
        }

        HasActiveQuery = true;

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

    // 左栏浏览：列当前目录一层，有自己的 CTS 与状态，不跟搜索抢 _searchCts。
    // 没有当前目录（首开无历史）就不列，提示就绪。
    private async Task RefreshLayerAsync()
    {
        if (string.IsNullOrEmpty(CurrentDirectory))
        {
            BrowseItems.Clear();
            HasNoBrowseResults = true;
            BrowseStatusMessage = Loc.Text(LangKey.FileSearchStatusReady);
            return;
        }

        _layerCts?.Cancel();
        _layerCts = new CancellationTokenSource();
        var layerCts = _layerCts;

        HasNoBrowseResults = false;
        BrowseStatusMessage = Loc.Text(LangKey.FileSearchStatusSearching);
        BrowseItems.Clear();

        try
        {
            LayerListing layer = await _searchService.ListLayerAsync(CurrentDirectory, layerCts.Token);

            if (!ReferenceEquals(_layerCts, layerCts)) return;

            if (layer.ErrorDetail != null)
            {
                HasNoBrowseResults = true;
                BrowseStatusMessage = string.Format(Loc.Text(LangKey.FileSearchStatusFailed), layer.ErrorDetail);
                return;
            }
            if (layer.Failure != null)
            {
                HasNoBrowseResults = true;
                BrowseStatusMessage = DescribeLayerFailure(layer);
                return;
            }

            var display = layer.Items.Take(MaxLayerItems).ToList();
            foreach (var item in display)
            {
                BrowseItems.Add(item);
            }

            HasNoBrowseResults = display.Count == 0;
            BrowseStatusMessage = string.Format(Loc.Text(LangKey.FileSearchStatusLayer), layer.Items.Count);
        }
        catch (OperationCanceledException)
        {
            // 被新一轮取代的取消不写状态；只有顶层取消才算一次取消
            if (ReferenceEquals(_layerCts, layerCts))
                BrowseStatusMessage = Loc.Text(LangKey.FileSearchStatusCancelled);
        }
    }

    // 浏览层的失败与搜索同构，文案也走同一套本地化
    private static string DescribeLayerFailure(LayerListing layer)
    {
        if (layer.Failure == null) return Loc.Text(LangKey.FileSearchStatusReady);
        return layer.Failure.Kind switch
        {
            ESearchFailureKind.PathNotFound => string.Format(
                Loc.Text(LangKey.FileSearchStatusDirectoryNotFound),
                layer.Failure.ResolvedDirectory),
            ESearchFailureKind.InvalidGlobPattern => string.Format(
                Loc.Text(LangKey.FileSearchStatusInvalidPattern),
                layer.Failure.Detail),
            _ => string.Format(Loc.Text(LangKey.FileSearchStatusFailed),
                layer.Failure.Detail),
        };
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
        // 浏览栏跟目录走、无条件刷新；有 query 时搜索根也变了，顺带重搜
        _ = RefreshLayerAsync();
        if (AutoSearch && !string.IsNullOrWhiteSpace(SearchQuery))
            DebounceSearch();
    }

    private void UpdateHistory(string path)
    {
        var existingIndex = HistoryDirectories.IndexOf(path);
        if (existingIndex == 0)
        {
            RefreshLocationState();
            return; // 已经是第一项，顺序没变，只同步侧栏选中
        }

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

        SyncRecentEntries();
        RefreshLocationState();
    }

    // 侧栏最近 = 历史目录的条目版，同批同步
    private void SyncRecentEntries()
    {
        RecentEntries.Clear();
        foreach (var dir in HistoryDirectories)
            RecentEntries.Add(new SidebarEntry(dir));
    }

    // 切目录后统一同步：面包屑由 OnCurrentDirectoryChanged 管，
    // 这里管侧栏选中与收藏态
    private void RefreshLocationState()
    {
        IsCurrentFavorite = !string.IsNullOrEmpty(CurrentDirectory) && _searchService.IsFavorite(CurrentDirectory);
        SyncSidebarSelection();
    }

    private void SyncSidebarSelection()
    {
        SelectedFavorite = FavoriteEntries.FirstOrDefault(f =>
            string.Equals(f.Path, CurrentDirectory, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>星标：收藏/取消收藏当前目录（按钮是 OneWay + Command，态由 IsCurrentFavorite 回推）</summary>
    [RelayCommand]
    private void ToggleFavorite()
    {
        if (string.IsNullOrEmpty(CurrentDirectory)) return;
        _searchService.ToggleFavorite(CurrentDirectory);
        RefreshFavoriteEntries();
        RefreshLocationState();
    }

    [RelayCommand]
    private void RemoveFavorite(SidebarEntry? entry)
    {
        if (entry == null) return;
        _searchService.RemoveFavorite(entry.Path);
        RefreshFavoriteEntries();
        RefreshLocationState();
    }

    private void RefreshFavoriteEntries()
    {
        FavoriteEntries.Clear();
        foreach (var dir in _searchService.GetFavorites())
            FavoriteEntries.Add(new SidebarEntry(dir));
        HasNoFavorites = FavoriteEntries.Count == 0;
    }

    [RelayCommand]
    private void RemoveRecent(SidebarEntry? entry)
    {
        if (entry == null) return;
        _searchService.RemoveHistory(entry.Path);
        HistoryDirectories.Remove(entry.Path);
        RecentEntries.Remove(entry);
        SyncSidebarSelection();
    }

    /// <summary>搜索框的 ✕：清空回到浏览态（OnSearchQueryChanged 收尾）</summary>
    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;

    [RelayCommand]
    private void OpenCurrentDirectory()
    {
        if (!string.IsNullOrEmpty(CurrentDirectory))
            App.FilesService.OpenFolder(CurrentDirectory);
    }

    /// <summary>浏览栏/面包屑里点目录条目下钻一层</summary>
    [RelayCommand]
    private void EnterDirectory(SearchItem? item)
    {
        if (item == null || !item.IsDirectory) return;
        string full = Path.IsPathFullyQualified(item.Path)
            ? Path.GetFullPath(item.Path)
            : Path.GetFullPath(Path.Combine(CurrentDirectory, item.Path));
        ApplyDirectory(full);
    }

    /// <summary>返回上一级（面包屑旁的 ↑）</summary>
    [RelayCommand]
    private void GoUp()
    {
        if (string.IsNullOrEmpty(CurrentDirectory)) return;
        string? parent = Path.GetDirectoryName(Path.GetFullPath(CurrentDirectory));
        if (!string.IsNullOrEmpty(parent)) ApplyDirectory(parent);
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
        // 左栏首开直接列当前目录一层（有历史目录的话）；没有则提示就绪
        _ = RefreshLayerAsync();
    }

    public void LoadHistory()
    {
        HistoryDirectories.Clear();
        foreach (var dir in _searchService.GetHistory())
        {
            HistoryDirectories.Add(dir);
        }
        SyncRecentEntries();

        RefreshFavoriteEntries();

        CurrentDirectory = HistoryDirectories.FirstOrDefault() ?? "";
        RefreshLocationState();
    }

    // 任何途径改当前目录（首开/下钻/回退/切根）都重建面包屑，收在这一个 hook
    partial void OnCurrentDirectoryChanged(string value) => RebuildBreadcrumb();

    // 面包屑 = 从根到当前的完整链（含根与自身）。根标全路径（/ 或 C:\），
    // 其余标文件夹名；末级是当前目录，只展示不跳转（IsCurrent）。
    private void RebuildBreadcrumb()
    {
        Breadcrumb.Clear();
        if (string.IsNullOrEmpty(CurrentDirectory)) return;

        List<string> chain = new();
        try
        {
            string? cur = Path.GetFullPath(CurrentDirectory);
            while (!string.IsNullOrEmpty(cur))
            {
                chain.Add(cur);
                string? parent = Path.GetDirectoryName(cur);
                if (parent == null || parent == cur) break;
                cur = parent;
            }
        }
        catch (Exception)
        {
            return; // 路径不成形就不画面包屑，目录框里还能改
        }
        chain.Reverse(); // 根..当前

        for (int i = 0; i < chain.Count; i++)
        {
            string full = chain[i];
            string label;
            if (i == 0)
            {
                label = full; // 根：unix "/"、windows "C:\"，全写才认得出来
            }
            else
            {
                string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                label = Path.GetFileName(trimmed);
                if (label.Length == 0) label = full;
            }
            Breadcrumb.Add(new DirSegment(label, full, i == chain.Count - 1));
        }
    }
}