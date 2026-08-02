using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Character;

public sealed class CharacterPromptConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("template_format")]
    public string TemplateFormat { get; set; } = "semantic-kernel";

    [JsonPropertyName("template")]
    public string? Template { get; set; }

    [JsonPropertyName("input_variables")]
    public List<object> InputVariables { get; set; } = [];

    [JsonPropertyName("execution_settings")]
    public Dictionary<string, object?> ExecutionSettings { get; set; } = [];

    [JsonPropertyName("allow_dangerously_set_content")]
    public bool AllowDangerouslySetContent { get; set; }
}

public class CharacterConfig
{
    public CharacterPromptConfig PromptConfig { get; set; } = new();
    public ChatPromptExecutionSettings ExecutionSettings { get; set; } = new();

    /// <summary>
    /// 组装为一个 ChatClientAgent
    /// </summary>
    /// <param name="chatClient">聊天客户端</param>
    /// <param name="instructions">已装配完成的系统提示词，由 CharacterPromptBuilder 产出</param>
    /// <returns>agent</returns>
    public ChatClientAgent ToAgent(IChatClient chatClient, string instructions)
    {
        ChatOptions chatOptions = ExecutionSettings.ToChatOptions();
        chatOptions.Instructions = instructions;

        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = PromptConfig.Name,
            Description = PromptConfig.Description,
            ChatOptions = chatOptions
        });
    }
}
