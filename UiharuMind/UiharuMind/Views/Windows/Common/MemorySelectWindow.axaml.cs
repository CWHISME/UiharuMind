using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Resources.Lang;
using Ursa.Controls;

namespace UiharuMind.Views.Windows.Common;

public partial class MemorySelectWindow : Window
{
    public MemorySelectWindow()
    {
        InitializeComponent();
    }
}

public partial class MemorySelectWindowModel : ObservableObject
{
    public ObservableCollection<MemoryData> Memories => App.MemoryService.MemorySources;

    private Action<MemoryData>? OnSelectMemory;

    [ObservableProperty] private MemoryData? _selectedMemory;
    [ObservableProperty] private string _selectedMemoryName;

    public MemorySelectWindowModel()
    {
    }

    public MemorySelectWindowModel(MemoryData? selectedMemory, Action<MemoryData>? onSelectMemory)
    {
        SelectedMemory = selectedMemory;
        OnSelectMemory = onSelectMemory;
        RefreshName();
    }

    [RelayCommand]
    private void SelectMemory(MemoryData memory)
    {
        OnSelectMemory?.Invoke(memory);
        SelectedMemory = memory;
        RefreshName();
    }

    [RelayCommand]
    private void EditMemory(MemoryData memory)
    {
        UIManager.ShowMemoryEditorWindow(UIManager.GetFoucusWindow(), memory);
    }

    [RelayCommand]
    private void DeletetMemory(MemoryData memory)
    {
        App.MessageService.ShowConfirmMessageBox(Lang.CommonDeleteConfirmTips,
            () => { MemoryManager.Instance.Delete(memory); });
    }

    [RelayCommand]
    private async Task CreateMemory()
    {
        var memoryName = await UIManager.ShowStringEditWindow("NewMemory");
        if (string.IsNullOrEmpty(memoryName))
        {
            return;
        }

        var item = MemoryManager.Instance.AddNewItem(memoryName);
        if (SelectedMemory == null) SelectMemory(item);
        EditMemory(item);
    }

    private void RefreshName()
    {
        SelectedMemoryName = SelectedMemory?.Name ?? Lang.NoMemory;
    }
}