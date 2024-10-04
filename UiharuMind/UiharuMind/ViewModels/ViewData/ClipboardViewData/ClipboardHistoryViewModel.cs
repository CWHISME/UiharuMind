using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Resources.Lang;

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

    private void OnClipboardStringChanged(string obj)
    {
        Refresh();
        OnPropertyChanged(nameof(ClipboardHistoryItems));
    }

    private void Refresh()
    {
        Title = string.Format(Lang.ClipboardHistoryCount, ClipboardHistoryItems.Count);
    }
}