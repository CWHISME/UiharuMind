using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.WorldSettings;
using UiharuMind.Features.Characters;

namespace UiharuMind.Features.WorldBooks;

/// <summary>
/// 世界书编辑窗：改一本共享书的条目与预算。每次改动即落盘（编辑器窗口没有「放弃」语义，
/// 关窗即保存——与角色草稿的「取消」不同，共享书不该有未落盘的中间态被别的角色读到）
/// </summary>
public partial class WorldBookEditorWindow : Window
{
    public WorldBookEditorWindow(string bookName)
    {
        InitializeComponent();
        WorldSetting? book = WorldSettingManager.Instance.Get(bookName);
        DataContext = book == null
            ? null
            : new WorldBookEditorWindowModel(book, Close);
    }

    private void OnSaveCloseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorldBookEditorWindowModel model) model.SaveClose();
    }
}

/// <summary>
/// 世界书编辑窗的数据模型：条目集合直接编辑、改动即同步回书并落盘
/// </summary>
public sealed partial class WorldBookEditorWindowModel : ObservableObject
{
    private readonly WorldSetting _book;
    private readonly Action _close;

    public string BookName => _book.Name;

    public ObservableCollection<WorldSettingEntryItem> Entries { get; }

    /// <summary>每轮注入预算（UI 下限 100）</summary>
    public int TokenBudget
    {
        get => _book.TokenBudget;
        set
        {
            _book.TokenBudget = Math.Max(100, value);
            OnPropertyChanged();
            Save();
        }
    }

    public WorldBookEditorWindowModel(WorldSetting book, Action close)
    {
        _book = book;
        _close = close;
        Entries = new ObservableCollection<WorldSettingEntryItem>(book.Entries.Select(WorldSettingEntryItem.FromEntry));
        foreach (WorldSettingEntryItem item in Entries) item.PropertyChanged += OnEntryEdited;
        Entries.CollectionChanged += OnEntriesChanged;
    }

    [RelayCommand]
    public void AddEntry() => Entries.Add(new WorldSettingEntryItem());

    [RelayCommand]
    public void RemoveEntry(WorldSettingEntryItem item) => Entries.Remove(item);

    [RelayCommand]
    public void SaveClose()
    {
        Save();
        _close();
    }

    private void OnEntryEdited(object? sender, PropertyChangedEventArgs e) => Sync();

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (WorldSettingEntryItem item in e.NewItems) item.PropertyChanged += OnEntryEdited;
        if (e.OldItems != null)
            foreach (WorldSettingEntryItem item in e.OldItems) item.PropertyChanged -= OnEntryEdited;
        Sync();
    }

    private void Sync()
    {
        _book.Entries = Entries.Select(x => x.ToEntry()).ToList();
        Save();
    }

    private void Save() => WorldSettingManager.Instance.Save(_book);
}