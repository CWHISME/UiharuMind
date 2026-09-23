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

    [JsonPropertyName("template")]
    public string? Template { get; set; }
}

public class CharacterConfig
{
    public CharacterPromptConfig PromptConfig { get; set; } = new();
    public ChatPromptExecutionSettings ExecutionSettings { get; set; } = new();

}
