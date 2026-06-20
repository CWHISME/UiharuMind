using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Services;

public class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private CultureInfo _currentCulture = LanguageUtils.GetSupportedCultureOrDefault(null);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        private set
        {
            if (_currentCulture.Name == value.Name) return;
            _currentCulture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageCode)));
            LanguageChanged?.Invoke();
        }
    }

    public string LanguageCode => CurrentCulture.Name;

    public void InitializeFromConfig()
    {
        ApplyLanguage(ConfigManager.Instance.Setting.LanguageCode, false);
    }

    public void ApplyLanguage(string? languageCode, bool save)
    {
        var cultureInfo = LanguageUtils.GetSupportedCultureOrDefault(languageCode);
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;
        Lang.Culture = cultureInfo;
        LanguageUtils.CurCultureInfo = cultureInfo;
        ApplyThemeLocale(cultureInfo.Name);

        if (save && ConfigManager.Instance.Setting.LanguageCode != cultureInfo.Name)
        {
            ConfigManager.Instance.Setting.LanguageCode = cultureInfo.Name;
        }

        CurrentCulture = cultureInfo;
    }

    public string GetString(string key)
    {
        return Lang.ResourceManager.GetString(key, CurrentCulture) ?? key;
    }

    private static void ApplyThemeLocale(string locale)
    {
        if (Application.Current == null) return;

        foreach (var style in Application.Current.Styles.OfType<IStyle>())
        {
            var localeProperty = style.GetType().GetProperty("Locale");
            if (localeProperty?.PropertyType == typeof(string) && localeProperty.CanWrite)
            {
                localeProperty.SetValue(style, locale);
            }
        }
    }
}
