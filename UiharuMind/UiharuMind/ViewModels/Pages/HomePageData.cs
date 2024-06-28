using Avalonia.Controls;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public class HomePageData : PageDataBase
{
    protected override Control CreateView => new HomePage();
}