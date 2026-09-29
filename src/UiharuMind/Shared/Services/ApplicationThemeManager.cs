using Avalonia;
using Avalonia.Styling;
using UiharuMind.Core.Configs;

namespace UiharuMind.Shared.Services;

public static class ApplicationThemeManager
{
    public const string DefaultThemeMode = "Default";
    public const string LightThemeMode = "Light";
    public const string DarkThemeMode = "Dark";

    public static readonly string[] SupportedThemeModes =
    [
        DefaultThemeMode,
        LightThemeMode,
        DarkThemeMode
    ];

    public static void InitializeFromConfig()
    {
        ApplyTheme(ConfigManager.Instance.Setting.ThemeMode, false);
    }

    public static void ApplyTheme(string? themeMode, bool save)
    {
        var normalizedThemeMode = NormalizeThemeMode(themeMode);
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = GetThemeVariant(normalizedThemeMode);
        }

        if (save && ConfigManager.Instance.Setting.ThemeMode != normalizedThemeMode)
        {
            ConfigManager.Instance.Setting.ThemeMode = normalizedThemeMode;
        }
    }

    public static string NormalizeThemeMode(string? themeMode)
    {
        return themeMode is LightThemeMode or DarkThemeMode
            ? themeMode
            : DefaultThemeMode;
    }

    public static ThemeVariant GetThemeVariant(string? themeMode)
    {
        return NormalizeThemeMode(themeMode) switch
        {
            LightThemeMode => ThemeVariant.Light,
            DarkThemeMode => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public static bool IsDarkTheme(ThemeVariant? themeVariant = null)
    {
        themeVariant ??= Application.Current?.ActualThemeVariant;
        return themeVariant == ThemeVariant.Dark;
    }
}
