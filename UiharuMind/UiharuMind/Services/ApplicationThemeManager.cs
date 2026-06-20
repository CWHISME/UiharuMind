using Avalonia;
using Avalonia.Styling;
using Semi.Avalonia;
using UiharuMind.Core.Configs;

namespace UiharuMind.Services;

public static class ApplicationThemeManager
{
    public const string DefaultThemeMode = "Default";
    public const string LightThemeMode = "Light";
    public const string DarkThemeMode = "Dark";
    public const string AquaticThemeMode = "Aquatic";
    public const string DesertThemeMode = "Desert";
    public const string DuskThemeMode = "Dusk";
    public const string NightSkyThemeMode = "NightSky";

    public static readonly string[] SupportedThemeModes =
    [
        DefaultThemeMode,
        LightThemeMode,
        DarkThemeMode,
        AquaticThemeMode,
        DesertThemeMode,
        DuskThemeMode,
        NightSkyThemeMode
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
        return themeMode is LightThemeMode or DarkThemeMode or AquaticThemeMode or DesertThemeMode or DuskThemeMode or NightSkyThemeMode
            ? themeMode
            : DefaultThemeMode;
    }

    public static ThemeVariant GetThemeVariant(string? themeMode)
    {
        return NormalizeThemeMode(themeMode) switch
        {
            LightThemeMode => ThemeVariant.Light,
            DarkThemeMode => ThemeVariant.Dark,
            AquaticThemeMode => SemiTheme.Aquatic,
            DesertThemeMode => SemiTheme.Desert,
            DuskThemeMode => SemiTheme.Dusk,
            NightSkyThemeMode => SemiTheme.NightSky,
            _ => ThemeVariant.Default
        };
    }

    public static bool IsDarkTheme(ThemeVariant? themeVariant = null)
    {
        themeVariant ??= Application.Current?.ActualThemeVariant;
        return themeVariant == ThemeVariant.Dark ||
               themeVariant == SemiTheme.Dusk ||
               themeVariant == SemiTheme.Aquatic ||
               themeVariant == SemiTheme.NightSky;
    }
}
