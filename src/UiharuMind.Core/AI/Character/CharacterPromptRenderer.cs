using System.Text.RegularExpressions;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Character;

public static partial class CharacterPromptRenderer
{
    [GeneratedRegex(@"\{\{\$(?<name>[A-Za-z0-9_]+)\}\}")]
    private static partial Regex VariableRegex();

    public static string Render(string template, IReadOnlyDictionary<string, object?>? arguments)
    {
        if (string.IsNullOrEmpty(template) || arguments == null || arguments.Count == 0) return template;

        return VariableRegex().Replace(template, match =>
        {
            string name = match.Groups["name"].Value;
            if (arguments.TryGetValue(name, out object? value))
                return value?.ToString() ?? "";

            Log.Warning($"Prompt variable was not provided: {name}");
            return match.Value;
        });
    }
}
