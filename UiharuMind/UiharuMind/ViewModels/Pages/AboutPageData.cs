using Avalonia.Controls;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class AboutPageData : PageDataBase
{
    public AboutPageData()
    {
    }

    protected override Control CreateView => new AboutPage();
}