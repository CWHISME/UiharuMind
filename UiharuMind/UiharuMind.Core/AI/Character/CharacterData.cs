using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace UiharuMind.Core.AI.Character;

public class CharacterData
{
    public CharacterConfig Config { get; set; } = new CharacterConfig();

    [Experimental("SKEXP0110")] public ChatCompletionAgent ChatAgent => Config.ToAgent();

    /// <summary>
    /// 角色名
    /// </summary>
    public string CharacterName
    {
        get => Config.PromptConfig.Name ?? "";
        set => Config.PromptConfig.Name = value;
    }

    /// <summary>
    /// 单纯的描述，似乎没有实际作用
    /// </summary>
    public string Description
    {
        get => Config.PromptConfig.Description ?? "";
        set => Config.PromptConfig.Description = value;
    }

    /// <summary>
    /// 角色的指令模板，会作为系统提示词的基础
    /// 参数由 {{$}} 构成，具体含义由具体的模板决定
    /// </summary>
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
}