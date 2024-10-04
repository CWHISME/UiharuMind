using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UiharuMind.ViewModels.ViewData.ClipboardViewData;

public partial class ClipboardSettingViewModel : ObservableObject
{
    private int _maxRecordCount;
    private bool _saveToDisk;
    private ClipboardHistoryViewModel _historyViewModel;

    public int MaxRecordCount
    {
        get => _maxRecordCount;
        set => SetProperty(ref _maxRecordCount, value);
    }

    public bool SaveToDisk
    {
        get => _saveToDisk;
        set => SetProperty(ref _saveToDisk, value);
    }

    public ClipboardSettingViewModel()
    {
        _historyViewModel = App.ViewModel.GetViewModel<ClipboardHistoryViewModel>();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _historyViewModel.ClipboardHistoryItems.Clear();
    }
}