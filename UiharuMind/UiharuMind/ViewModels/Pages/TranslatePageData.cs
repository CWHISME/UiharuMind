using Avalonia.Controls;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public class TranslatePageData : PageDataBase
{
    protected override Control CreateView => new TranslatePage();
}