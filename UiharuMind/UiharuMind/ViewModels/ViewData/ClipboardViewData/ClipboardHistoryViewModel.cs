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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Resources.Lang;
using Ursa.Controls;

namespace UiharuMind.ViewModels.ViewData.ClipboardViewData;

public partial class ClipboardHistoryViewModel : ObservableObject
{
    public ObservableCollection<ClipboardItem> ClipboardHistoryItems => App.Clipboard.ClipboardHistoryItems;

    [ObservableProperty] private string _title;

    public ClipboardHistoryViewModel()
    {
        Refresh();
        App.Clipboard.OnClipboardStringChanged += OnClipboardStringChanged;
    }

    public void Copy(ClipboardItem item)
    {
        ClipboardHistoryItems.Remove(item);
        item.CopyToClipboard();
    }

    public void Delete(ClipboardItem item)
    {
        ClipboardHistoryItems.Remove(item);
    }

    public void DeleteAll()
    {
        App.Clipboard.ClearClipboardHistory();
        Refresh();
    }

    private void OnClipboardStringChanged(string obj)
    {
        Refresh();
        // OnPropertyChanged(nameof(ClipboardHistoryItems));
    }

    private void Refresh()
    {
        Title = string.Format(Lang.ClipboardHistoryCount, ClipboardHistoryItems.Count);
    }
}