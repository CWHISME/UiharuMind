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
            PerformSearch(false);
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
        Dispatcher.UIThread.Post(() =>
        {
            App.Clipboard.ClipboardHistoryItems.Remove(item);
            item.CopyToClipboard();
        }, DispatcherPriority.ApplicationIdle);
    }

    public void Delete(ClipboardItem item)
    {
        App.Clipboard.ClipboardHistoryItems.Remove(item);
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
        if (delay) _debounceTimer = new Timer(_ => { Dispatcher.UIThread.Invoke(() => PerformSearch(_searchText)); }, null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        else Dispatcher.UIThread.Invoke(() => PerformSearch(_searchText));
    }

    private void PerformSearch(string value)
    {
        if (!IsSearchActive && FilteredClipboardHistoryItems.Count == App.Clipboard.ClipboardHistoryItems.Count && FilteredClipboardHistoryItems.Count > 0 && FilteredClipboardHistoryItems[0] == App.Clipboard.ClipboardHistoryItems[0])
        {
            return;
        }

        FilteredClipboardHistoryItems.Clear();

        if (!IsSearchActive || string.IsNullOrWhiteSpace(value))
        {
            // 显示所有项目
            foreach (var item in App.Clipboard.ClipboardHistoryItems)
            {
                FilteredClipboardHistoryItems.Add(item);
            }
        }
        else
        {
            // 执行过滤
            var filtered = App.Clipboard.ClipboardHistoryItems.Where(item =>
                item.Text.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var item in filtered)
            {
                FilteredClipboardHistoryItems.Add(item);
            }
        }

        RefreshTitle();
    }

    private void RefreshTitle()
    {
        if (IsSearchActive)
        {
            Title = string.Format(Lang.ClipboardHistoryCount, FilteredClipboardHistoryItems.Count + "/" + App.Clipboard.ClipboardHistoryItems.Count);
            return;
        }

        Title = string.Format(Lang.ClipboardHistoryCount, App.Clipboard.ClipboardHistoryItems.Count);
    }
}