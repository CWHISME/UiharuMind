using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace UiharuMind.ViewModels;

public enum ControlStatus
{
    New,
    Beta,
    Stable,
}

public class MenuItemViewData : ViewModelBase
{
    public string? MenuHeader { get; set; }
    public string? MenuIconName { get; set; }
    public string? Key { get; set; }
    public string? Status { get; set; }

    public bool IsSeparator { get; set; }
    public ObservableCollection<MenuItemViewData> Children { get; set; } = new();

    public ICommand ActivateCommand { get; set; }

    public MenuItemViewData()
    {
        ActivateCommand = new RelayCommand(OnActivate);
    }

    private void OnActivate()
    {
        if (IsSeparator || Key == null) return;
        WeakReferenceMessenger.Default.Send<string>(Key);
    }
}