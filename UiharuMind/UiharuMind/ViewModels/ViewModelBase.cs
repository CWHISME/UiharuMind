using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.ViewModels;

public class ViewModelBase : ObservableRecipient
{
    protected ViewModelBase()
    {
        IsActive = true;
    }

    public virtual void OnEnable()
    {
        IsActive = true;
    }

    public virtual void OnDisable()
    {
        IsActive = false;
    }

    // protected virtual bool AutoActive => true;
}