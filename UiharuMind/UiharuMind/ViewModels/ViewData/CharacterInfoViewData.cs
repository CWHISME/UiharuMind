using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.ViewModels.ViewData;

public partial class CharacterInfoViewData : ObservableObject
{
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
        }
    }

    public string DialogTemplate
    {
        get => _characterData.DialogTemplate;
        set
        {
            _characterData.DialogTemplate = value;
            OnPropertyChanged();
        }
    }

    public string FirstGreeting
    {
        get => _characterData.FirstGreeting;
        set
        {
            _characterData.FirstGreeting = value;
            OnPropertyChanged();
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

    public CharacterInfoViewData()
    {
        _characterData = new CharacterData();
    }

    public CharacterInfoViewData(CharacterData characterData)
    {
        _characterData = characterData;
        Name = characterData.CharacterName;
        Description = characterData.Description;
    }

    [RelayCommand]
    public void StartChat()
    {
        Log.Debug("Start chat with " + Name);
    }

    [RelayCommand]
    public void EditCharacter()
    {
        Log.Debug("Edit character " + Name);
    }
}