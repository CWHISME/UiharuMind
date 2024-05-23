using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.ViewModels.Interfaces;

namespace UiharuMind.ViewModels.Pages;

public abstract partial class PageDataBase : ViewModelBase, IViewControl
{
    private Control? _view;

    public Control View
    {
        get { return _view ??= CreateView; }
    }

    protected abstract Control CreateView { get; }
}