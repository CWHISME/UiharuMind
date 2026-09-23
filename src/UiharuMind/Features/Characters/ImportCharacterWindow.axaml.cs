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
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Character.CharacterCards;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Features.Characters;

public partial class ImportCharacterWindow : Window
{
    // 链接导入的反馈行：解锁失败时显示「暂不可用」，避免重复点击堆叠
    private TextBlock? _urlImportResultTip;

    public ImportCharacterWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 链接导入：目前只认开发者网址——输入它静默解锁屏蔽角色，其余一律不动。
    /// 解锁成功不给提示（角色列表里出现屏蔽角色即反馈）；其余输入补一行「暂不可用」，
    /// 让“写着暂不支持却可点击”的界面行为与文案一致。地址与解锁逻辑在
    /// <see cref="CharacterVisibility"/>。
    /// </summary>
    private void OnImportFromUrlBtnClick(object? sender, RoutedEventArgs e)
    {
        if (CharacterVisibility.TryUnlock(ImportUrlInput.Text))
        {
            RemoveUrlImportResultTip();
            return;
        }

        if (_urlImportResultTip == null)
        {
            _urlImportResultTip = new TextBlock
            {
                Text = Loc.Text(LangKey.ImportUrlDisabledTitle),
                Foreground = new SolidColorBrush(Colors.OrangeRed),
                TextWrapping = TextWrapping.Wrap,
            };
            ImportUrlListPanel.Children.Add(_urlImportResultTip);
        }
    }

    private void RemoveUrlImportResultTip()
    {
        if (_urlImportResultTip == null) return;
        ImportUrlListPanel.Children.Remove(_urlImportResultTip);
        _urlImportResultTip = null;
    }

    private async void OnImportFromFileBtnClick(object? sender, RoutedEventArgs e)
    {
        var characteData = await ImportCharacter();
        if (characteData == null) return;

        // 标识是新生成的 GUID,不可能撞车,因此不再有"重名了要不要改"那一步
        characteData.NormalizeParams();
        if (!CharacterManager.Instance.TryAddNewCharacterData(characteData)) return;

        ImportListPanel.Children.Add(new TextBlock()
            { Text = characteData.CharacterName + "   Imported!", Foreground = new SolidColorBrush(Colors.LimeGreen) });
    }

    public async Task<CharacterData?> ImportCharacter()
    {
        var file = await App.FilesService.OpenFileAsync(UIManager.GetFocusWindow(), "*.json");
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
            return character;
        }
        catch (Exception e)
        {
            Log.Error("Failed to import character, error:" + e.Message);
            return null;
        }
    }
}
