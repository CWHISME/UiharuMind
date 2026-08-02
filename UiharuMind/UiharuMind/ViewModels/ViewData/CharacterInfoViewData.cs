using System;
using UiharuMind.Core.AI.Chat;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.Views;
using UiharuMind.Views.Windows.Characters;
using Ursa.Controls;

namespace UiharuMind.ViewModels.ViewData;

public partial class CharacterInfoViewData : ObservableObject
{
    private readonly IMessageService _messageService;

    /// <summary>
    /// 内联挂载的角色。存的是标识，界面显示解析后的名字。
    /// </summary>
    public ObservableCollection<MountedCharacterItem> MountPrompts { get; }

    /// <summary>
    /// 角色标识（挂载引用与会话引用都用它，改名不会断链）
    /// </summary>
    public string CharacterId => _characterData.CharacterId;

    public bool IsDefault => _characterData.IsDefaultCharacter;

    /// <summary>
    /// 是否为普通(扮演)角色。由挂载列表派生——挂了提示词片段就是扮演角色，
    /// 没挂就是纯提示词的工具型角色。要改变它请增删挂载项，而不是翻一个旗标。
    /// </summary>
    public bool IsRole => !_characterData.IsPurePromptCharacter;

    /// <summary>
    /// 是否为 agent 类角色(装配工具与工作目录)
    /// </summary>
    public bool IsAgent
    {
        get => _characterData.Kind == ECharacterKind.Agent;
        set
        {
            _characterData.Kind = value ? ECharacterKind.Agent : ECharacterKind.Roleplay;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 所属功能名
    /// </summary>
    public string FuncName => IsRole ? Lang.RoleplayCharacter : Lang.Tool;

    public IImmutableSolidColorBrush FuncColor =>
        IsRole ? Avalonia.Media.Brushes.LightGreen : Avalonia.Media.Brushes.Gold;

    public long FileDateTime => _characterData.FileDateTime;

    public string Name
    {
        get => _characterData.CharacterName;
        set
        {
            _characterData.CharacterName = value;
            OnPropertyChanged();
        }
    }

    public Bitmap? Icon
    {
        get => IconUtils.GetCharacterBitmapOrDefault(_characterData);
        set
        {
            _characterData.CharacterIcon = value.BitmapToBase64();
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
        : this(characterData, App.Services.GetRequiredService<IMessageService>())
    {
    }

    public CharacterInfoViewData(CharacterData characterData, IMessageService messageService)
    {
        _messageService = messageService;
        _characterData = characterData;
        Name = characterData.CharacterName;
        Description = characterData.Description;

        MountPrompts = new ObservableCollection<MountedCharacterItem>(
            characterData.MountPrompts.Select(MountedCharacterItem.FromId));
        MountPrompts.CollectionChanged += (sender, args) =>
        {
            _characterData.MountPrompts = MountPrompts.Select(x => x.Id).ToList();
            OnPropertyChanged(nameof(IsRole));
            OnPropertyChanged(nameof(Icon));
        };
    }

    public async void TryAddToNewCharacterData(Action? onSuccess = null)
    {
        ParamsSaveValidReplacer();
        if (!CharacterManager.Instance.TryAddNewCharacterData(_characterData))
        {
            if (await _messageService.ConfirmAsync(Lang.AddDuplicateCharacterTips))
                UIManager.ShowEditCharacterWindow(this, x => TryAddToNewCharacterData(onSuccess));
        }
        else
        {
            _messageService.ShowNotification(
                Lang.AddCharacterSuccessTips, severity: MessageSeverity.Success);
            onSuccess?.Invoke();
        }
    }

    public bool CheckCharacterNameValid()
    {
        if (string.IsNullOrEmpty(_characterData.CharacterName))
        {
            _ = _messageService.ShowErrorAsync(Lang.CharacterEmptyNameTips);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 保存之前的参数的有效性检查并替换，避免填错参数导致的错误
    /// </summary>
    public void ParamsSaveValidReplacer()
    {
        Template = _characterData.ParamsValidReplacer(Template);
        DialogTemplate = _characterData.ParamsValidReplacer(DialogTemplate);
        FirstGreeting = _characterData.ParamsValidReplacer(FirstGreeting);
    }

    [RelayCommand]
    public void StartChat()
    {
        // Log.Debug("Start chat with " + Name);
        // App.ViewModel.GetViewModel<ChatListViewModel>().StartNewSession(_characterData);
        SessionManager.Instance.StartNewSession(_characterData);
        App.JumpToPage(MenuPages.MenuChatKey);
        // WeakReferenceMessenger.Default.Send(MenuKeys.MenuChatKey);
    }

    [RelayCommand]
    public void EditCharacter()
    {
        UIManager.ShowEditCharacterWindow(this, x => x.SaveCharacter());
    }

    [RelayCommand]
    public void SaveCharacter()
    {
        if (CheckCharacterNameValid())
        {
            ParamsSaveValidReplacer();
            _characterData.Save();
        }
    }

    [RelayCommand]
    public async Task DeleteCharacter()
    {
        if (await _messageService.ConfirmAsync(
                string.Format(Lang.CharacterDeleteTips, _characterData.CharacterName)))
            _characterData.Delete();
    }

    [RelayCommand]
    public async Task CopyCharacter()
    {
        if (await _messageService.ConfirmAsync(
                string.Format(Lang.CharacterCopyTips, _characterData.CharacterName)))
            _characterData.Copy();
    }

    [RelayCommand]
    public async Task AddMountCharacter()
    {
        HashSet<string> alreadySelectedList = new HashSet<string>(MountPrompts.Select(x => x.Id));
        var result = await CharacterSelectWindow.ShowCharacterSelectWindow(UIManager.GetFocusWindow(),
            alreadySelectedList, CharacterSelectWindow.CharacterType.Tool, CharacterId);
        if (result != null)
        {
            MountPrompts.Clear();
            //排除重复及自己
            HashSet<string> selectedList = new HashSet<string>(
                result.Select(x => x.CharacterId).Where(x => x != CharacterId));
            foreach (var selected in selectedList)
                MountPrompts.Add(MountedCharacterItem.FromId(selected));
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
    //     MountPrompts.Clear();
    //     MountPrompts.AddRange(target.MountPrompts);
    // }
}

/// <summary>
/// 挂载项的显示包装：数据里存的是角色标识，界面上要显示可读的角色名
/// </summary>
public sealed class MountedCharacterItem
{
    /// <summary>被挂载角色的标识</summary>
    public string Id { get; }

    /// <summary>被挂载角色的显示名；角色不存在时回退为标识本身</summary>
    public string Name { get; }

    private MountedCharacterItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>
    /// 由角色标识解析出显示名
    /// </summary>
    /// <param name="characterId">角色标识</param>
    /// <returns>挂载项</returns>
    public static MountedCharacterItem FromId(string characterId)
    {
        string name = CharacterManager.Instance.GetCharacterData(characterId).CharacterName;
        return new MountedCharacterItem(characterId, string.IsNullOrEmpty(name) ? characterId : name);
    }
}
