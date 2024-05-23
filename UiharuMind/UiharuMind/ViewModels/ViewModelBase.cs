using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.ViewModels;

public class ViewModelBase : ObservableRecipient
{
    protected ViewModelBase()
    {
        IsActive = true;
    }

    // protected virtual bool AutoActive => true;
}