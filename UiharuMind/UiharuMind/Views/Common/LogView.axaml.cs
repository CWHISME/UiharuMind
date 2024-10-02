using Avalonia.Controls;
using UiharuMind.ViewModels.UIHolder;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

public partial class LogView : UserControl
{

    private readonly ScrollViewerAutoScrollHolder _scrollHolder;

    public LogView()
    {
        InitializeComponent();
        
        DataContext = App.ViewModel.GetViewModel<LogViewModel>();
        _scrollHolder = new ScrollViewerAutoScrollHolder(Viewer);
    }
}