using System;
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
/// 列表里的一个仓库
/// </summary>
/// <param name="Repository">owner/repo</param>
/// <param name="DownloadsText">下载量</param>
public sealed record ModelRepoListItem(string Repository, string DownloadsText)
{
    /// <summary>
    /// 仓库名（不含作者）
    /// </summary>
    public string Name => Repository[(Repository.IndexOf('/') + 1)..];

    /// <summary>
    /// 作者
    /// </summary>
    public string Owner => Repository.Contains('/') ? Repository[..Repository.IndexOf('/')] : "";
}

/// <summary>
/// 「获取模型」页签：左边搜仓库，右边看仓库详情，底部下载区。
/// 第一次选中页签才建出来，网络请求也从那时才发
/// </summary>
public partial class ModelDownloadPageData : ObservableObject
{
    private const int SearchLimit = 30;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(400);

    private readonly ModelDownloadContext _context;
    private IModelSource _source;
    private string _sourceKey;
    private CancellationTokenSource? _searchCancellation;
    private bool _hasSearched;
    private string _resultsQuery = ""; //当前结果是按哪个词搜出来的

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string? _searchError;
    [ObservableProperty] private bool _isResultEmpty;
    [ObservableProperty] private string _sourceLabel = "";
    [ObservableProperty] private ModelRepoListItem? _selectedItem;
    [ObservableProperty] private ModelRepoDetailData? _detail;

    public ModelDownloadPageData(ModelDownloadContext context)
    {
        _context = context;
        _source = context.CreateSource();
        _sourceKey = SourceKey();
        SourceLabel = Loc.Text(LangKey.ModelDownloadSourceLabel, SourceDisplayName());
    }

    /// <summary>
    /// 搜索结果
    /// </summary>
    public ObservableCollection<ModelRepoListItem> Results { get; } = [];

    /// <summary>
    /// 列表上方的小标题：按当前结果的来路，空搜索搜出来的是热门仓库
    /// </summary>
    public string ListCaption => Loc.Text(string.IsNullOrWhiteSpace(_resultsQuery)
        ? LangKey.ModelDownloadPopularCaption
        : LangKey.ModelDownloadResultsCaption);

    /// <summary>
    /// 下载区
    /// </summary>
    public DownloadQueueViewData Queue => _context.QueueView;

    /// <summary>
    /// 页签被选中时调：首次发起搜索；下载源设置改过则重来
    /// </summary>
    public void Activate()
    {
        string key = SourceKey();
        if (_hasSearched && key == _sourceKey) return;
        _sourceKey = key;
        _source = _context.CreateSource();
        SourceLabel = Loc.Text(LangKey.ModelDownloadSourceLabel, SourceDisplayName());
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

    partial void OnSelectedItemChanged(ModelRepoListItem? value)
    {
        if (value != null && value.Repository != Detail?.Repository) OpenRepository(value.Repository);
    }

    [RelayCommand]
    private void CloseDetail()
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
            var results = await Task.Run(() => source.SearchAsync(query.Trim(), SearchLimit, cancellation.Token),
                cancellation.Token);
            if (cancellation.IsCancellationRequested) return;

            _resultsQuery = query;
            OnPropertyChanged(nameof(ListCaption));
            Results.Clear();
            foreach (ModelRepoSummary summary in results)
                Results.Add(new ModelRepoListItem(summary.Repository, FormatDownloads(summary.Downloads)));
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
            IsResultEmpty = false;
            SearchError = Loc.Text(LangKey.ModelDownloadSearchFailed, e.Message);
        }

        if (_searchCancellation == cancellation) IsSearching = false;
    }

    private static string FormatDownloads(long downloads) => downloads switch
    {
        >= 1_000_000 => $"{downloads / 1_000_000.0:0.#}M",
        >= 1_000 => $"{downloads / 1_000.0:0.#}K",
        _ => downloads.ToString()
    };

    private static string SourceKey()
    {
        DownloadSourceSettingConfig config = DownloadSourceSettingConfig.Current;
        return $"{config.ModelSource}|{config.CustomEndpoint}|{config.HuggingFaceToken}|{config.ModelScopeToken}";
    }

    private static string SourceDisplayName()
    {
        DownloadSourceSettingConfig config = DownloadSourceSettingConfig.Current;
        return config.ModelSource switch
        {
            DownloadSourceSettingConfig.SourceHfMirror => "hf-mirror",
            DownloadSourceSettingConfig.SourceModelScope => Loc.Text(LangKey.ModelSourceModelScope),
            DownloadSourceSettingConfig.SourceCustom when Uri.TryCreate(config.CustomEndpoint.Trim(), UriKind.Absolute,
                out Uri? uri) => uri.Host,
            _ => "HuggingFace"
        };
    }
}
