using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.Views.Windows.Common;

public partial class MemoryCreateWindow : Window
{
    public MemoryCreateWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MemoryCreateWindowModel { CanCreate: true } model) return;
        Close(new MemoryCreateRequest(model.Name.Trim(), model.Description.Trim()));
    }
}

public partial class MemoryCreateWindowModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private string _name = "";

    [ObservableProperty] private string _description = "";

    public bool CanCreate => !string.IsNullOrWhiteSpace(Name) &&
                             Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                             Name is not "." and not "..";
    public bool HasNameError => !string.IsNullOrWhiteSpace(Name) && !CanCreate;
}

public sealed record MemoryCreateRequest(string Name, string Description);
