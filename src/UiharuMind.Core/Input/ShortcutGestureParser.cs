using SharpHook.Data;

namespace UiharuMind.Core.Input;

public static class ShortcutGestureParser
{
    public static bool TryParse(string? gesture, out KeyCode mainKey, out List<KeyCode> modifiers, out string error)
    {
        mainKey = KeyCode.VcUndefined;
        modifiers = new List<KeyCode>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            error = "Shortcut is empty.";
            return false;
        }

        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "Shortcut is empty.";
            return false;
        }

        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var modifier))
            {
                if (!modifiers.Contains(modifier)) modifiers.Add(modifier);
                continue;
            }

            if (!TryParseMainKey(part, out var keyCode))
            {
                error = $"Unknown key: {part}.";
                return false;
            }

            if (mainKey != KeyCode.VcUndefined)
            {
                error = "Shortcut can only contain one non-modifier key.";
                return false;
            }

            mainKey = keyCode;
        }

        if (mainKey == KeyCode.VcUndefined)
        {
            error = "Shortcut must contain a main key.";
            return false;
        }

        return true;
    }

    public static string Normalize(string gesture)
    {
        return TryParse(gesture, out var mainKey, out var modifiers, out _)
            ? ToDisplayString(mainKey, modifiers)
            : gesture.Trim();
    }

    public static string ToDisplayString(KeyCode mainKey, IEnumerable<KeyCode>? modifiers)
    {
        var parts = new List<string>();
        if (modifiers != null)
        {
            foreach (var modifier in modifiers)
            {
                var display = ModifierToDisplayName(modifier);
                if (display != null && !parts.Contains(display)) parts.Add(display);
            }
        }

        if (mainKey != KeyCode.VcUndefined)
        {
            parts.Add(KeyToDisplayName(mainKey));
        }

        return string.Join("+", parts);
    }

    public static bool IsModifierKey(KeyCode keyCode)
    {
        return ModifierToDisplayName(keyCode) != null;
    }

    private static bool TryParseModifier(string text, out KeyCode keyCode)
    {
        keyCode = text.Trim().ToLowerInvariant() switch
        {
            "alt" or "option" => KeyCode.VcLeftAlt,
            "shift" => KeyCode.VcLeftShift,
            "ctrl" or "control" => KeyCode.VcLeftControl,
            "cmd" or "command" or "meta" or "win" or "windows" => KeyCode.VcLeftMeta,
            _ => KeyCode.VcUndefined
        };

        return keyCode != KeyCode.VcUndefined;
    }

    private static bool TryParseMainKey(string text, out KeyCode keyCode)
    {
        var normalized = text.Trim();
        keyCode = KeyCode.VcUndefined;

        if (normalized.Length == 1)
        {
            var character = char.ToUpperInvariant(normalized[0]);
            if (character is >= 'A' and <= 'Z')
            {
                return Enum.TryParse($"Vc{character}", out keyCode);
            }

            if (character is >= '0' and <= '9')
            {
                return Enum.TryParse($"Vc{character}", out keyCode);
            }
        }

        var aliases = normalized.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "return" => "Enter",
            "spacebar" => "Space",
            "pageup" => "PageUp",
            "pagedown" => "PageDown",
            "backquote" or "`" => "BackQuote",
            "-" => "Minus",
            "=" => "Equals",
            "[" => "OpenBracket",
            "]" => "CloseBracket",
            "\\" => "Backslash",
            ";" => "Semicolon",
            "'" => "Quote",
            "," => "Comma",
            "." => "Period",
            "/" => "Slash",
            _ => normalized
        };

        return Enum.TryParse($"Vc{aliases}", true, out keyCode);
    }

    private static string? ModifierToDisplayName(KeyCode keyCode)
    {
        return keyCode switch
        {
            KeyCode.VcLeftAlt or KeyCode.VcRightAlt => "Alt",
            KeyCode.VcLeftShift or KeyCode.VcRightShift => "Shift",
            KeyCode.VcLeftControl or KeyCode.VcRightControl => "Ctrl",
            KeyCode.VcLeftMeta or KeyCode.VcRightMeta => "Meta",
            _ => null
        };
    }

    private static string KeyToDisplayName(KeyCode keyCode)
    {
        var name = keyCode.ToString();
        return name.StartsWith("Vc", StringComparison.Ordinal) ? name[2..] : name;
    }
}
