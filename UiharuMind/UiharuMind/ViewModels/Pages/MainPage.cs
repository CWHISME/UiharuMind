using Avalonia.Interactivity;

namespace UiharuMind.ViewModels;

public partial class MainPage : ViewModelBase
{
   
    public string Title { get; set; } = "UiharuMind";
    public int Count { get; set; } = 0;

    public MainPage(string title)
    {
        Title = title;
    }

    public void IncrementCount()
    {
        Count++;
    }

    public void ClickHandler(object sender, RoutedEventArgs args)
    {
        Count++;
    }
}
