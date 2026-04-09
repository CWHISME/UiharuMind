/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Views;
using UiharuMind.Views.Windows;
using Ursa.Controls;

namespace UiharuMind.ViewModels.ViewData.ClipboardViewData;

public partial class ClipboardHistoryViewModel : ViewModelBase
{
    // public ObservableCollection<ClipboardItem> ClipboardHistoryItems => App.Clipboard.ClipboardHistoryItems;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private ObservableCollection<ClipboardItem> _filteredClipboardHistoryItems;

    // private List<ClipboardItem> _allItems;

    private bool _isSearchActive;

    public bool IsSearchActive
    {
        get => _isSearchActive;
        set
        {
            SetProperty(ref _isSearchActive, value);
            if (value) IsImageFilterActive = false;
            else PerformSearch(false);
        }
    }

    private bool _isImageFilterActive;

    /// <summary>
    /// 筛选仅显示图片
    /// </summary>
    public bool IsImageFilterActive
    {
        get => _isImageFilterActive;
        set
        {
            SetProperty(ref _isImageFilterActive, value);
            if (value) IsSearchActive = false;
            else PerformSearch(false);
        }
    }

    private string _searchText = string.Empty;
    private Timer? _debounceTimer; // 防抖定时器

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            PerformSearch();
        }
    }

    public ClipboardHistoryViewModel()
    {
        // _allItems = new List<ClipboardItem>(App.Clipboard.ClipboardHistoryItems);
        FilteredClipboardHistoryItems = new ObservableCollection<ClipboardItem>(App.Clipboard.ClipboardHistoryItems);

        RefreshTitle();
        // App.Clipboard.OnClipboardStringChanged += OnClipboardStringChanged;
    }

    public void SyncData()
    {
        PerformSearch(false);
    }

    public void Copy(ClipboardItem item)
    {
        UIManager.CloseWindow<QuickClipboardHistoryWindow>();
        Dispatcher.UIThread.Post(item.CopyToClipboard, DispatcherPriority.ApplicationIdle);
    }

    public void Delete(ClipboardItem item)
    {
        App.Clipboard.DeleteClipboardHistoryItem(item);
    }

    public void DeleteAll()
    {
        App.Clipboard.ClearClipboardHistory();
        IsSearchActive = false;
        PerformSearch("");
    }

    // private void OnClipboardStringChanged(string obj)
    // {
    //     // 更新所有项目缓存
    //     // _allItems = new List<ClipboardItem>(App.Clipboard.ClipboardHistoryItems);
    //
    //     PerformSearch(_searchText);
    //     // OnPropertyChanged(nameof(ClipboardHistoryItems));
    // }

    private void PerformSearch(bool delay = true)
    {
        // 使用防抖机制，减少频繁搜索
        _debounceTimer?.Dispose();
        if (delay) _debounceTimer = new Timer(_ => { Dispatcher.UIThread.Invoke(() => PerformSearch(SearchText)); }, null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        else Dispatcher.UIThread.Invoke(() => PerformSearch(SearchText));
    }

    private void PerformSearch(string value)
    {
        var sourceItems = App.Clipboard.ClipboardHistoryItems;

        // 如果没有激活任何过滤条件，显示所有项目
        if (!IsSearchActive && !IsImageFilterActive)
        {
            if (IsCollectionEqual(FilteredClipboardHistoryItems, sourceItems))
            {
                return;
            }

            UpdateFilteredItems(sourceItems);
            RefreshTitle();
            return;
        }

        // 执行过滤
        IEnumerable<ClipboardItem> filtered = sourceItems;

        // 图片过滤
        if (IsImageFilterActive)
        {
            filtered = filtered.Where(item => item.IsImage);
        }

        // 文本搜索过滤
        if (IsSearchActive && !string.IsNullOrWhiteSpace(value))
        {
            filtered = filtered.Where(item =>
                !string.IsNullOrEmpty(item.Text) &&
                item.Text.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();

        // 只在结果变化时更新
        if (!IsCollectionEqual(FilteredClipboardHistoryItems, filteredList))
        {
            UpdateFilteredItems(filteredList);
        }

        RefreshTitle();
    }

    private bool IsCollectionEqual(ObservableCollection<ClipboardItem> collection, IEnumerable<ClipboardItem> items)
    {
        var itemList = items.ToList();
        if (collection.Count != itemList.Count)
        {
            return false;
        }

        if (collection.Count > 0)
        {
            if (!collection[0].Equals(itemList[0])) return false;
        }

        return true;
    }

    private void UpdateFilteredItems(IEnumerable<ClipboardItem> items)
    {
        FilteredClipboardHistoryItems.Clear();
        foreach (var item in items)
        {
            FilteredClipboardHistoryItems.Add(item);
        }
    }

    private void RefreshTitle()
    {
        if (IsSearchActive || IsImageFilterActive)
        {
            Title = string.Format(Lang.ClipboardHistoryCount, FilteredClipboardHistoryItems.Count + "/" + App.Clipboard.ClipboardHistoryItems.Count);
            return;
        }

        Title = string.Format(Lang.ClipboardHistoryCount, App.Clipboard.ClipboardHistoryItems.Count);
    }
}