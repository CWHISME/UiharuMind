using Avalonia.Controls;
using UiharuMind.Shared.Data;

namespace UiharuMind.Shared.Controls;

public partial class ModelRuntimeBasicSettingsView : UserControl
{
    public ModelRuntimeBasicSettingsView()
    {
        InitializeComponent();
        DataContext ??= new ModelRuntimeBasicSettingsData();
    }
}
