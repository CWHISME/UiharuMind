using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.ViewModels.ViewData;

public partial class CharacterListViewModel : ObservableObject
{
    public List<CharacterInfoViewModel> Characters { get; set; } = new();

    [ObservableProperty] private CharacterInfoViewModel _selectedCharacter;
    
   public CharacterListViewModel()
   {
       LoadCharacters();
   }

   private void LoadCharacters()
   {
       
   }
}