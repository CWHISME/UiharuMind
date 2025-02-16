using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.Characters;

public partial class ImportCharacterWindow : Window
{
    public ImportCharacterWindow()
    {
        InitializeComponent();
    }

    private void OnImportFromUrlBtnClick(object? sender, RoutedEventArgs e)
    {
    }

    private async void OnImportFromFileBtnClick(object? sender, RoutedEventArgs e)
    {
        var characteData = await ImportCharacter();
        if (characteData == null) return;

        characteData.TryAddToNewCharacterData(() =>
        {
            ImportListPanel.Children.Add(new TextBlock()
                { Text = characteData.Name + "   Imported!", Foreground = new SolidColorBrush(Colors.LimeGreen) });
        });
    }

    public async Task<CharacterInfoViewData?> ImportCharacter()
    {
        var file = await App.FilesService.OpenFileAsync(UIManager.GetFoucusWindow(), "*.json");
        if (file == null) return null;
        try
        {
            var stream = await file.OpenReadAsync();
            using TextReader reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            var character = await CharacterCardImporter.ImportToCharactorData(content);
            if (character == null)
            {
                Log.Error("Failed to import character, content is invalid.");
                return null;
            }

            if (string.IsNullOrEmpty(character.CharacterName))
                character.CharacterName = Path.GetFileNameWithoutExtension(file.Name);
            return new CharacterInfoViewData(character);
        }
        catch (Exception e)
        {
            Log.Error("Failed to import character, error:" + e.Message);
            return null;
        }
    }
}