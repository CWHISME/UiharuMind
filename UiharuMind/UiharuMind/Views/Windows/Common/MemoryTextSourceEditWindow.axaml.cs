using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;

namespace UiharuMind.Views.Windows.Common;

public partial class MemoryTextSourceEditWindow : Window
{
    public MemoryTextSourceEditWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MemoryTextSourceEditWindowModel model || !model.CanSave) return;
        Close(model.CreateResult());
    }
}

public partial class MemoryTextSourceEditWindowModel : ObservableObject
{
    private readonly string _id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _title = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ContentLengthText))]
    private string _content = "";

    public bool CanSave => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Content);
    public string ContentLengthText => string.Format(
        Lang.ResourceManager.GetString("MemoryTextCharacterCount",
            LocalizationManager.Instance.CurrentCulture) ?? "{0}",
        Content.Length);

    public MemoryTextSourceEditWindowModel()
    {
        _id = Guid.NewGuid().ToString("N");
    }

    public MemoryTextSourceEditWindowModel(MemoryTextSource? source)
    {
        _id = source?.Id ?? Guid.NewGuid().ToString("N");
        Title = source?.Title ?? "";
        Content = source?.Content ?? "";
    }

    public MemoryTextSource CreateResult() => new()
    {
        Id = _id,
        Title = Title.Trim(),
        Content = Content.Trim()
    };
}
