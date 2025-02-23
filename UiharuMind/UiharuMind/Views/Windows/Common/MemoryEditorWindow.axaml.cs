using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.Views.Windows.Common;

public partial class MemoryEditorWindow : Window
{
    public MemoryEditorWindow()
    {
        InitializeComponent();
    }
}

public partial class MemoryEditorWindowModel : ObservableObject
{
    
    public ObservableCollection<KnowledgeItem> KnowledgeItems { get; set; } = new();
    
}

public enum KnowledgeType
{
    File,
    Directory,
    Url,
    Text
}

public class KnowledgeItem
{
    public string Title { get; set; }
    public KnowledgeType Type { get; set; }
    public string SourcePath { get; set; }
    public string Content { get; set; }
    public DateTime AddedDate { get; set; }
    public string Metadata { get; set; }
}