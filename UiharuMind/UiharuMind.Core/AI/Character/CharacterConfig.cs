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

    public ChatClientAgent ToAgent(IChatClient chatClient, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        string instructions = CharacterPromptRenderer.Render(PromptConfig.Template ?? "", arguments);
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
