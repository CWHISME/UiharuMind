using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

#pragma warning disable SKEXP0110

namespace UiharuMind.Core.AI.Character;

public class CharacterData
{
    public CharacterConfig Config { get; set; } = new CharacterConfig();

    public ChatCompletionAgent ToAgent(Kernel kernel, Dictionary<string, object?>? kernelArguments = null) =>
        Config.ToAgent(kernel, CheckParams(kernelArguments));

    /// <summary>
    /// 是否是默认角色
    /// </summary>
    public bool IsDefaultCharacter { get; set; }

    /// <summary>
    /// 在角色列表隐藏
    /// </summary>
    public bool IsHide { get; set; }

    /// <summary>
    /// 是否是工具人
    /// 当该值为 false 时，才会使用 MountCharacters
    /// 当该值为 true 时，则只会使用 Template 作为系统提示，其它参数将会直接忽略
    /// </summary>
    public bool IsTool { get; set; }

    /// <summary>
    /// 是否携带历史对话的上下文，如果为false则不携带，每次只有最后一句用户消息
    /// 注：仅工具角色有效，角色扮演 必定携带历史上下文
    /// </summary>
    public bool IsNotTakeHistoryContext { get; set; }

    /// <summary>
    /// 额外挂载工具角色(只能挂载没有挂载过对象的角色、即纯工具角色)
    /// 若挂载对象被挂载了额外角色，则该挂载对象将会失效
    /// </summary>
    public List<string> MountCharacters { get; set; } =
        new List<string>()
        {
            DefaultCharacter.Actor.ToString(),
            DefaultCharacter.RoleplaySimple.ToString(),
            DefaultCharacter.RoleplayDetailed.ToString(),
            DefaultCharacter.RoleplayImmersive.ToString()
        };

    /// <summary>
    /// 角色名
    /// </summary>
    [JsonIgnore]
    public string CharacterName
    {
        get => Config.PromptConfig.Name ?? "";
        set => Config.PromptConfig.Name = value;
    }

    /// <summary>
    /// 单纯的描述，在作为工具人被某个agent调用时，会作为其功能描述
    /// </summary>
    [JsonIgnore]
    public string Description
    {
        get => Config.PromptConfig.Description ?? "";
        set => Config.PromptConfig.Description = value;
    }

    /// <summary>
    /// 角色的指令模板，会作为系统提示词的基础
    /// 参数由 {{$}} 构成，具体含义由具体的模板决定
    /// </summary>
    [JsonIgnore]
    public string Template
    {
        get => Config.PromptConfig.Template ?? "";
        set => Config.PromptConfig.Template = value;
    }

    /// <summary>
    /// 对话模板，主要用于角色扮演，可选，会作为系统回复的基础
    /// </summary>
    public string DialogTemplate { get; set; } = "";

    /// <summary>
    /// 开场白，可选，会作为系统回复的开头
    /// </summary>
    public string FirstGreeting { get; set; } = "";

    /// <summary>
    /// 尝试将指定内容的占位内容替换为实际内容
    /// {{$char}} 代表角色名
    /// {{$user}} 代表用户名
    /// </summary>
    /// <param name="template"></param>
    public string TryRender(string template)
    {
        return template.Replace("{{$char}}", CharacterName)
            .Replace("{{$user}}", CharacterManager.Instance.UserCharacterName);
    }

    public void Save()
    {
        CharacterManager.Instance.SaveCharacterData(this);
    }

    public void Copy()
    {
        var newCharData = DeepCopy();
        int index = 0;
        do
        {
            newCharData.CharacterName = newCharData.CharacterName + "_Copy" + index++;
        } while (!CharacterManager.Instance.TryAddNewCharacterData(newCharData));
    }

    public void Delete()
    {
        CharacterManager.Instance.DeleteCharacterData(this);
    }

    public static implicit operator ChatCompletionAgent(CharacterData characterData)
    {
        return characterData.ToAgent(LlmManager.Instance.CurrentRunningModel!.Kernel);
    }


    // ============================== Common Params ================================

    public const string ParamsNameLanguage = "lang";
    public const string ParamsNameLanguageDefault = "lang_default";
    public const string ParamsNameChar = "char";
    public const string ParamsNameUser = "user";

    private Dictionary<string, object?> CheckParams(Dictionary<string, object?>? kernelArguments)
    {
        kernelArguments ??= new Dictionary<string, object?>();
        kernelArguments.TryAdd(ParamsNameLanguage, LanguageUtils.CurCultureInfo.NativeName);
        kernelArguments.TryAdd(ParamsNameLanguageDefault, LanguageUtils.CurCultureInfo.NativeName);
        kernelArguments.TryAdd(ParamsNameChar, CharacterName);
        kernelArguments.TryAdd(ParamsNameUser, CharacterManager.Instance.UserCharacterName);
        return kernelArguments;
    }

    //================================================================================

    public CharacterData DeepCopy()
    {
        var tmpStr = SaveUtility.SaveToString(this);
        return (SaveUtility.LoadFromString<CharacterData>(tmpStr));
    }
}