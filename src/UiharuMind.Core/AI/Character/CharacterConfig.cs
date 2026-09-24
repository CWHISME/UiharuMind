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

    /// <summary>
    /// 手写人格锚点：系统提示末尾回锚句的显式写法，为空则回退到名 + 描述自动拼。
    /// 第二人称、短祈使，只写风格不写事实（见 <c>CharacterData.GetPersonaCoda</c>）。
    /// </summary>
    [JsonPropertyName("anchor")]
    public string? Anchor { get; set; }
}

public class CharacterConfig
{
    public CharacterPromptConfig PromptConfig { get; set; } = new();
    public ChatPromptExecutionSettings ExecutionSettings { get; set; } = new();

}
