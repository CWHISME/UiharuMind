using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 「获取模型」的 GGUF 仓库分类：左边搜 HF / 魔搭仓库（滚到底续下一页），右边看仓库详情。
/// 第一次被选中才发网络请求
/// </summary>
public partial class GgufMarketData : ObservableObject, IModelMarket
{
    private const int SearchLimit = 30;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(400);

    private readonly ModelDownloadContext _context;
    private IModelSource _source;
    private string _sourceKey;
    private CancellationTokenSource? _searchCancellation;
    private bool _hasSearched;
    private string _resultsQuery = ""; //当前结果是按哪个词搜出来的
    private EModelSearchSort _resultsSort; //当前结果的排序，续页沿用
    private string? _nextCursor; //下一页游标，没有更多为 null
    private readonly HashSet<string> _listed = new(StringComparer.OrdinalIgnoreCase); //已列出的仓库，翻页时去重

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private string? _searchError;
    [ObservableProperty] private bool _isResultEmpty;
    [ObservableProperty] private string _sourceLabel = "";
    [ObservableProperty] private ModelSearchSortOption _selectedSort;
    [ObservableProperty] private ModelRepoListItem? _selectedItem;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private ModelRepoDetailData? _detail;

    public GgufMarketData(ModelDownloadContext context)
    {
        _context = context;
        _source = context.CreateSource();
        _sourceKey = ModelDownloadSourceInfo.Key();
        SortOptions = ModelSearchSortOption.CreateAll();
        _selectedSort = SortOptions[0];
        SourceLabel = ModelDownloadSourceInfo.Label();
    }

    public string Title => Loc.Text(LangKey.ModelMarketGguf);

    public bool HasDetail => Detail != null;

    /// <summary>
    /// 搜索结果
    /// </summary>
    public ObservableCollection<ModelRepoListItem> Results { get; } = [];

    /// <summary>
    /// 排序选项
    /// </summary>
    public ModelSearchSortOption[] SortOptions { get; }

    /// <summary>
    /// 被选中时调：首次发起搜索；下载源设置改过则重来
    /// </summary>
    public void Activate()
    {
        string key = ModelDownloadSourceInfo.Key();
        if (_hasSearched && key == _sourceKey) return;
        _sourceKey = key;
        _source = _context.CreateSource();
        SourceLabel = ModelDownloadSourceInfo.Label();
        CloseDetail();
        _hasSearched = true;
        // 从别处带着仓库名跳过来（如 owner/repo）时直接打开，热门列表照样拉
        _ = SearchAsync(ModelRepoReference.TryParse(SearchText, out string repository) ? "" : SearchText, TimeSpan.Zero);
        if (repository.Length > 0) OpenRepository(repository);
    }

    /// <summary>
    /// 打开某个仓库的详情
    /// </summary>
    /// <param name="repository">owner/repo</param>
    public void OpenRepository(string repository)
    {
        Detail?.Dispose();
        ModelRepoDetailData detail = new(_context, _source, repository);
        Detail = detail;
        _ = detail.LoadAsync();
    }

    /// <summary>
    /// 续取下一页（列表滚到底时调）；正在搜、正在取或没有更多时什么也不做
    /// </summary>
    public void LoadNextPage()
    {
        if (IsSearching || IsLoadingMore || _nextCursor == null || _searchCancellation == null) return;
        _ = LoadNextPageAsync(_nextCursor, _searchCancellation);
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_hasSearched) return;
        if (ModelRepoReference.TryParse(value, out string repository))
        {
            _searchCancellation?.Cancel();
            IsSearching = false;
            OpenRepository(repository);
            return;
        }

        _ = SearchAsync(value, SearchDelay);
    }

    // 换排序立刻重搜；搜索框里是仓库引用时按空关键词列
    partial void OnSelectedSortChanged(ModelSearchSortOption value)
    {
        if (!_hasSearched) return;
        _ = SearchAsync(ModelRepoReference.TryParse(SearchText, out _) ? "" : SearchText, TimeSpan.Zero);
    }

    partial void OnSelectedItemChanged(ModelRepoListItem? value)
    {
        if (value != null && value.Repository != Detail?.Repository) OpenRepository(value.Repository);
    }

    /// <summary>
    /// 关掉仓库详情
    /// </summary>
    public void CloseDetail()
    {
        Detail?.Dispose();
        Detail = null;
        SelectedItem = null;
    }

    [RelayCommand]
    private void OpenSourceSettings() => _context.OpenSourceSettings();

    private async Task SearchAsync(string query, TimeSpan delay)
    {
        _searchCancellation?.Cancel();
        CancellationTokenSource cancellation = new();
        _searchCancellation = cancellation;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellation.Token);
            IsSearching = true;
            SearchError = null;
            IModelSource source = _source;
            EModelSearchSort sort = SelectedSort.Sort;
            ModelSearchPage page = await Task.Run(
                () => source.SearchAsync(query.Trim(), sort, SearchLimit, null, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;

            _resultsQuery = query;
            _resultsSort = sort;
            Results.Clear();
            _listed.Clear();
            AppendPage(page);
            IsResultEmpty = Results.Count == 0;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            Log.Warning($"Model search failed: {e.Message}");
            Results.Clear();
            _nextCursor = null;
            IsResultEmpty = false;
            SearchError = Loc.Text(LangKey.ModelDownloadSearchFailed, e.Message);
        }

        if (_searchCancellation == cancellation) IsSearching = false;
    }

    // 续页失败不清已有结果，只记日志；留着游标，下次滚到底再试
    private async Task LoadNextPageAsync(string cursor, CancellationTokenSource cancellation)
    {
        IsLoadingMore = true;
        try
        {
            IModelSource source = _source;
            string query = _resultsQuery.Trim();
            EModelSearchSort sort = _resultsSort;
            ModelSearchPage page = await Task.Run(
                () => source.SearchAsync(query, sort, SearchLimit, cursor, cancellation.Token), cancellation.Token);
            if (!cancellation.IsCancellationRequested) AppendPage(page);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Warning($"Model search next page failed: {e.Message}");
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private void AppendPage(ModelSearchPage page)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (ModelRepoSummary summary in page.Items)
            if (_listed.Add(summary.Repository))
                Results.Add(ModelRepoListItem.From(summary, _resultsSort, now));
        _nextCursor = page.NextCursor;
    }
}
