// Root myDeserializedClass = JsonSerializer.Deserialize<Root>(myJsonResponse);

using System.Text.Json.Serialization;

namespace UiharuMind.Core.AI.Character.CharacterCards;

public class Data
{
    [JsonPropertyName("avatar")] public string? Avatar { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("first_mes")] public string? FirstMes { get; set; }

    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("creator")] public string? Creator { get; set; }

    [JsonPropertyName("creator_notes")] public string? CreatorNotes { get; set; }

    [JsonPropertyName("alternate_greetings")]
    public List<object>? AlternateGreetings { get; set; }

    [JsonPropertyName("character_version")]
    public string? CharacterVersion { get; set; }

    [JsonPropertyName("mes_example")] public string? MesExample { get; set; }

    [JsonPropertyName("post_history_instructions")]
    public string? PostHistoryInstructions { get; set; }

    [JsonPropertyName("system_prompt")] public string? SystemPrompt { get; set; }

    [JsonPropertyName("scenario")] public string? Scenario { get; set; }

    [JsonPropertyName("personality")] public string? Personality { get; set; }

    [JsonPropertyName("extensions")] public Extensions? Extensions { get; set; }

    [JsonPropertyName("group_only_greetings")]
    public List<object>? GroupOnlyGreetings { get; set; }
}

public class DepthPrompt
{
    [JsonPropertyName("prompt")] public string? Prompt { get; set; }

    [JsonPropertyName("depth")] public int? Depth { get; set; }

    [JsonPropertyName("role")] public string? Role { get; set; }
}

public class Extensions
{
    [JsonPropertyName("depth_prompt")] public DepthPrompt? DepthPrompt { get; set; }

    [JsonPropertyName("pygmalion_id")] public string? PygmalionId { get; set; }

    [JsonPropertyName("fav")] public bool? Fav { get; set; }

    [JsonPropertyName("talkativeness")] public string? Talkativeness { get; set; }

    [JsonPropertyName("world")] public string? World { get; set; }
}

public class CharacterCard
{
    [JsonPropertyName("spec")] public string? Spec { get; set; }

    [JsonPropertyName("spec_version")] public string? SpecVersion { get; set; }

    [JsonPropertyName("data")] public Data? Data { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("fav")] public bool? Fav { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("personality")] public string? Personality { get; set; }

    [JsonPropertyName("scenario")] public string? Scenario { get; set; }

    [JsonPropertyName("first_mes")] public string? FirstMes { get; set; }

    [JsonPropertyName("mes_example")] public string? MesExample { get; set; }

    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }

    [JsonPropertyName("chat")] public string? Chat { get; set; }

    [JsonPropertyName("create_date")] public string? CreateDate { get; set; }

    [JsonPropertyName("creatorcomment")] public string? Creatorcomment { get; set; }

    [JsonPropertyName("avatar")] public string? Avatar { get; set; }

    [JsonPropertyName("talkativeness")] public string? Talkativeness { get; set; }
}