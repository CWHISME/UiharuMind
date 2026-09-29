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

    /// <summary>
    /// 出自哪部作品（单文本，不带书名号，渲染时再加）。
    /// 群场景段按它把同作品成员合并介绍；角色搜索也认它。
    /// </summary>
    [JsonPropertyName("works")]
    public string? Works { get; set; }

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
