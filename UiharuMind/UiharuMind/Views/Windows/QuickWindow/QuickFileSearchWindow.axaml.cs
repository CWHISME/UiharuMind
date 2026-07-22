using System;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UiharuMind.Core.AI.Agent.Files;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class QuickFileSearchWindow : UiharuWindowBase
{
    private SimpleGlobber _glob;
    private SimpleGrepper _grepper;
    private string _searchRoot;
    private bool _isContentMode;
    private CancellationTokenSource? _searchCts;
    private ObservableCollection<string> _results = new();

    public QuickFileSearchWindow()
    {
        InitializeComponent();
        
        _searchRoot = Environment.CurrentDirectory;
        _glob = new SimpleGlobber(_searchRoot);
        _grepper = new SimpleGrepper(_searchRoot);
        
        ResultsList.ItemsSource = _results;
        DataContext = this;
    }

    public static void Show()
    {
        UIManager.ShowWindow<QuickFileSearchWindow>();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SearchBox.Focus();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        
        var query = SearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            _results.Clear();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (_isContentMode)
                {
                    var searchResults = await _grepper.SearchAsync(
                        query, 
                        ct: _searchCts.Token);
                    
                    _results.Clear();
                    foreach (var result in searchResults)
                    {
                        _results.Add($"{result.FileName}: {result.Snippet}");
                    }
                }
                else
                {
                    var searchResults = await _glob.SearchAsync(
                        $"**/*{query}*", 
                        ct: _searchCts.Token);
                    
                    _results.Clear();
                    foreach (var result in searchResults)
                    {
                        _results.Add(result);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _results.Clear();
                _results.Add($"Error: {ex.Message}");
            }
        });
    }

    private void OnModeToggleClick(object? sender, RoutedEventArgs e)
    {
        _isContentMode = ModeToggle.IsChecked == true;
        ModeToggle.Content = _isContentMode ? "Name" : "Content";
        
        // Re-trigger search with new mode
        var query = SearchBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(query))
        {
            SearchBox.Text = "";
            SearchBox.Text = query;
        }
    }
}