using Avalonia.Controls;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public class LogPageData : PageDataBase
{
    protected override Control CreateView => new LogPage();
}