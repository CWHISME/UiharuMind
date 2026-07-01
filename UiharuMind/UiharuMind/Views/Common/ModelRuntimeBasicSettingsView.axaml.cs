using Avalonia.Controls;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

public partial class ModelRuntimeBasicSettingsView : UserControl
{
    public ModelRuntimeBasicSettingsView()
    {
        InitializeComponent();
        DataContext ??= new ModelRuntimeBasicSettingsData();
    }
}
