/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Core.Clipboard;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 剪贴板历史列表。<b>分页</b>加载，筛选与搜索都下沉到 SQL——
/// 因此搜的是全部历史，而不是「已经加载进列表的那一页」。
///
/// <para>
/// 防抖与「取消上一次在途查询」两条是从 <c>SearchViewModel</c> 搬过来的：
/// 不取消的话，慢查询的结果会晚到并覆盖掉新查询的结果，表现为列表跳回上一个词。
/// </para>
/// </summary>
public partial class ClipboardHistoryViewModel : ViewModelBase
{
    private const int PageSize = 200;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>当前已加载的条目。翻页只往尾部 <c>Add</c>，不重建</summary>
    public ObservableCollection<ClipboardItem> Items { get; } = new();

    [ObservableProperty] private string _title = "";

    private long? _cursor; //下一页的游标,取上一页最后一条的排序键
    private bool _hasMore = true;
    private bool _isLoading;
    private int _generation; //每次重来都 +1,用来丢弃晚到的旧结果
    private DispatcherTimer? _debounceTimer;

    private bool _isSearchActive;

    public bool IsSearchActive
    {
        get => _isSearchActive;
        set
        {
            SetProperty(ref _isSearchActive, value);
            if (value)
            {
                IsImageFilterActive = false;
                IsFavoriteFilterActive = false;
            }

            Reload();
        }
    }

    private bool _isImageFilterActive;

    public bool IsImageFilterActive
    {
        get => _isImageFilterActive;
        set
        {
            SetProperty(ref _isImageFilterActive, value);
            if (value)
            {
                IsSearchActive = false;
                IsFavoriteFilterActive = false;
            }

            Reload();
        }
    }

    private bool _isFavoriteFilterActive;

    public bool IsFavoriteFilterActive
    {
        get => _isFavoriteFilterActive;
        set
        {
            SetProperty(ref _isFavoriteFilterActive, value);
            if (value)
            {
                IsSearchActive = false;
                IsImageFilterActive = false;
            }

            Reload();
        }
    }

    private string _searchText = string.Empty;

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            ReloadDebounced();
        }
    }

    public ClipboardHistoryViewModel()
    {
        App.Clipboard.OnClipboardChanged += OnClipboardChanged;
        Reload();
    }

    /// <summary>窗口再次打开时重新拉取</summary>
    public void SyncData() => Reload();

    public void Copy(ClipboardItem item)
    {
        UIManager.CloseWindow<QuickClipboardHistoryWindow>();
        Dispatcher.UIThread.Invoke(item.CopyToClipboard, DispatcherPriority.ApplicationIdle);
    }

    public void Delete(ClipboardItem item)
    {
        App.Clipboard.DeleteClipboardHistoryItems([item.Id]);
        Items.Remove(item); //只摘掉这一条,不整页重建
        RefreshTitle();
    }

    public void ToggleFavorite(ClipboardItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        App.Clipboard.History.SetFavorite(item.Id, item.IsFavorite);
        if (IsFavoriteFilterActive && !item.IsFavorite) Items.Remove(item);
        RefreshTitle();
    }

    public void DeleteAll()
    {
        //如果处于筛选中，仅删除当前筛选出的结果
        if (IsSearchActive || IsImageFilterActive || IsFavoriteFilterActive)
        {
            List<long> ids = [];
            foreach (ClipboardItem item in Items) ids.Add(item.Id);
            App.Clipboard.DeleteClipboardHistoryItems(ids);
        }
        else App.Clipboard.ClearClipboardHistory();

        Reload();
    }

    /// <summary>
    /// 续取下一页。列表滚到接近底部时调用，重复调用是安全的
    /// </summary>
    public void LoadNextPage()
    {
        if (_isLoading || !_hasMore) return;
        _ = LoadPageAsync(_generation);
    }

    /// <summary>重新从第一页开始加载</summary>
    public void Reload()
    {
        _generation++;
        _cursor = null;
        _hasMore = true;
        _isLoading = false;
        Items.Clear();
        _ = LoadPageAsync(_generation);
    }

    private void ReloadDebounced()
    {
        _debounceTimer?.Stop();
        _debounceTimer = new DispatcherTimer { Interval = SearchDebounce };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer?.Stop();
            Reload();
        };
        _debounceTimer.Start();
    }

    private async Task LoadPageAsync(int generation)
    {
        _isLoading = true;
        try
        {
            ClipboardHistoryFilter filter = BuildFilter();
            long? cursor = _cursor;
            List<ClipboardHistoryEntry> page =
                await Task.Run(() => App.Clipboard.History.GetPage(filter, cursor, PageSize));

            if (generation != _generation) return; //筛选条件已经变了,这一页是上一次查询的结果

            foreach (ClipboardHistoryEntry entry in page) Items.Add(new ClipboardItem(entry));
            if (page.Count > 0) _cursor = page[^1].SortKey;
            _hasMore = page.Count == PageSize;
            RefreshTitle();
        }
        finally
        {
            if (generation == _generation) _isLoading = false;
        }
    }

    private ClipboardHistoryFilter BuildFilter() => new(
        IsSearchActive ? SearchText : null,
        IsImageFilterActive,
        IsFavoriteFilterActive);

    // 只在用户还停在第一页时才自动刷新:已经往下翻了还整页重建的话,
    // 正在看的位置会被一次复制操作顶掉
    private void OnClipboardChanged()
    {
        if (Items.Count > PageSize) return;
        if (Dispatcher.UIThread.CheckAccess()) Reload();
        else Dispatcher.UIThread.Post(Reload);
    }

    private void RefreshTitle()
    {
        int total = App.Clipboard.History.Count(ClipboardHistoryFilter.None);
        if (IsSearchActive || IsImageFilterActive || IsFavoriteFilterActive)
        {
            string loaded = _hasMore ? $"{Items.Count}+" : Items.Count.ToString();
            Title = string.Format(Lang.ClipboardHistoryCount, $"{loaded}/{total}");
            return;
        }

        Title = string.Format(Lang.ClipboardHistoryCount, total);
    }
}
