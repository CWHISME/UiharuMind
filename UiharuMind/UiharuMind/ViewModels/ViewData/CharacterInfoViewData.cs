using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Views;
using UiharuMind.Views.Windows.Characters;
using Ursa.Controls;

namespace UiharuMind.ViewModels.ViewData;

public partial class CharacterInfoViewData : ObservableObject
{
    public ObservableCollection<string> MountCharacters { get; }

    public bool IsDefault => _characterData.IsDefaultCharacter;

    /// <summary>
    /// 是否为普通角色，否则为工具人
    /// </summary>
    public bool IsRole
    {
        get => !_characterData.IsTool;
        set
        {
            _characterData.IsTool = !value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 所属功能名
    /// </summary>
    public string FuncName => IsRole ? Lang.RoleplayCharacter : Lang.Tool;

    public IImmutableSolidColorBrush FuncColor =>
        IsRole ? Avalonia.Media.Brushes.LightGreen : Avalonia.Media.Brushes.Gold;

    public string Name
    {
        get => _characterData.CharacterName;
        set
        {
            _characterData.CharacterName = value;
            OnPropertyChanged();
        }
    }

    public string Description
    {
        get => _characterData.Description;
        set
        {
            _characterData.Description = value;
            OnPropertyChanged();
        }
    }

    public string Template
    {
        get => _characterData.Template;
        set
        {
            _characterData.Template = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TemplateReadonly));
        }
    }

    public string DialogTemplate
    {
        get => _characterData.DialogTemplate;
        set
        {
            _characterData.DialogTemplate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DialogTemplateReadonly));
        }
    }

    public string FirstGreeting
    {
        get => _characterData.FirstGreeting;
        set
        {
            _characterData.FirstGreeting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstGreetingReadonly));
        }
    }

    public string TemplateReadonly =>
        string.IsNullOrEmpty(Template) ? "无" : _characterData.TryRender(Template);

    public string FirstGreetingReadonly =>
        string.IsNullOrEmpty(FirstGreeting) ? "无" : _characterData.TryRender(FirstGreeting);

    public string DialogTemplateReadonly =>
        string.IsNullOrEmpty(DialogTemplate) ? "无" : _characterData.TryRender(DialogTemplate);

    public ChatPromptExecutionSettings ChatPromptExecutionSettings
    {
        get => _characterData.Config.ExecutionSettings;
        set
        {
            _characterData.Config.ExecutionSettings = value;
            OnPropertyChanged();
        }
    }

    //================对话参数设置=================
    // public double? Temperature
    // {
    //     get => _characterData.Config.OpenAiSettings.Temperature;
    //     set
    //     {
    //         _characterData.Config.OpenAiSettings.Temperature = value;
    //         OnPropertyChanged();
    //     }
    // }
    //
    // public double? TopP
    // {
    //     get => _characterData.Config.OpenAiSettings.TopP;
    //     set
    //     {
    //         _characterData.Config.OpenAiSettings.TopP = value;
    //         OnPropertyChanged();
    //     }
    // }
    //
    // // public int? MaxTokens
    // // {
    // //     get => _characterData.Config.OpenAiSettings.MaxTokens;
    // //     set
    // //     {
    // //         _characterData.Config.OpenAiSettings.MaxTokens = value;
    // //         OnPropertyChanged();
    // //     }
    // // }
    //
    // public double? PresencePenalty
    // {
    //     get => _characterData.Config.OpenAiSettings.PresencePenalty;
    //     set
    //     {
    //         _characterData.Config.OpenAiSettings.PresencePenalty = value;
    //         OnPropertyChanged();
    //     }
    // }
    //
    // public double? FrequencyPenalty
    // {
    //     get => _characterData.Config.OpenAiSettings.FrequencyPenalty;
    //     set
    //     {
    //         _characterData.Config.OpenAiSettings.FrequencyPenalty = value;
    //         OnPropertyChanged();
    //     }
    // }
    //
    // /// <summary>
    // /// 同样的对话种子可以产生同样的回复
    // /// </summary>
    // public long? Seed
    // {
    //     get => _characterData.Config.OpenAiSettings.Seed;
    //     set
    //     {
    //         _characterData.Config.OpenAiSettings.Seed = value;
    //         OnPropertyChanged();
    //     }
    // }
    //=================================================

    private CharacterData _characterData;

    public CharacterInfoViewData() : this(new CharacterData())
    {
    }

    public CharacterInfoViewData(CharacterData characterData)
    {
        _characterData = characterData;
        Name = characterData.CharacterName;
        Description = characterData.Description;

        MountCharacters = new ObservableCollection<string>(characterData.MountCharacters);
        MountCharacters.CollectionChanged += (sender, args) =>
        {
            _characterData.MountCharacters = MountCharacters.ToList();
        };
    }

    public void TryAddToNewCharacterData()
    {
        if (!CharacterManager.Instance.TryAddNewCharacterData(_characterData))
        {
            App.MessageService.ShowConfirmMessageBox(Lang.AddDuplicateCharacterTips,
                () => { CharacterEditWindow.Show(this, x => TryAddToNewCharacterData()); });
        }
    }

    [RelayCommand]
    public void StartChat()
    {
        // Log.Debug("Start chat with " + Name);
        // App.ViewModel.GetViewModel<ChatListViewModel>().StartNewSession(_characterData);
        ChatManager.Instance.StartNewSession(_characterData);
        App.JumpToPage(MenuPages.MenuChatKey);
        // WeakReferenceMessenger.Default.Send(MenuKeys.MenuChatKey);
    }

    [RelayCommand]
    public void EditCharacter()
    {
        CharacterEditWindow.Show(this, x => x.SaveCharacter());
    }

    [RelayCommand]
    public void SaveCharacter()
    {
        _characterData.Save();
    }

    [RelayCommand]
    public void DeleteCharacter()
    {
        App.MessageService.ShowConfirmMessageBox(
            string.Format(Lang.CharacterDeleteTips, _characterData.CharacterName), () => { _characterData.Delete(); });
    }

    [RelayCommand]
    public void CopyCharacter()
    {
        App.MessageService.ShowConfirmMessageBox(
            string.Format(Lang.CharacterCopyTips, _characterData.CharacterName), () => { _characterData.Copy(); });
    }

    [RelayCommand]
    public async Task AddMountCharacter()
    {
        HashSet<string> alreadySelectedList = new HashSet<string>(MountCharacters);
        var result = await CharacterSelectWindow.Show(UIManager.GetFoucusWindow(), alreadySelectedList,
            CharacterSelectWindow.CharacterType.Tool, Name);
        if (result != null)
        {
            MountCharacters.Clear();
            //排除重复及自己
            HashSet<string> selectedList = new HashSet<string>(result.Select(x => x.Name).Where(x => x != Name));
            MountCharacters.AddRange(selectedList);
        }
    }

    // public CharacterInfoViewData DeepCopy()
    // {
    //     var tmpStr = SaveUtility.SaveToString(_characterData);
    //     return new CharacterInfoViewData(SaveUtility.LoadFromString<CharacterData>(tmpStr));
    // }
    //
    // public void CopyFrom(CharacterInfoViewData target)
    // {
    //     Name = target.Name;
    //     Description = target.Description;
    //     Template = target.Template;
    //     DialogTemplate = target.DialogTemplate;
    //     FirstGreeting = target.FirstGreeting;
    //     ChatPromptExecutionSettings.Temperature= target.ChatPromptExecutionSettings.Temperature;
    //     
    //     MountCharacters.Clear();
    //     MountCharacters.AddRange(target.MountCharacters);
    // }
}