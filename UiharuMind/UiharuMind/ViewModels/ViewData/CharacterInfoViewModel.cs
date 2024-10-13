using CommunityToolkit.Mvvm.ComponentModel;

namespace UiharuMind.ViewModels.ViewData;

public class CharacterInfoViewModel : ObservableObject
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string ImageUrl { get; set; }
}