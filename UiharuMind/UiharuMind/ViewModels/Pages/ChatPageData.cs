using Avalonia.Controls;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public class ChatPageData : PageDataBase
{
    protected override Control CreateView => new ChatPage();
}