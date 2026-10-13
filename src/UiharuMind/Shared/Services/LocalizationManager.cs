using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using UiharuMind.Resources.Lang;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;

namespace UiharuMind.Shared.Services;

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
        ApplyThemeLocale(cultureInfo);

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

    /// <summary>强类型入口：key 由枚举成员编译期约束，运行时按成员名回查。</summary>
    public string GetString(LangKey key)
    {
        return GetString(key.ToString());
    }

    // 主题自带的文案（消息框按钮等）跟着界面语言走。两套主题的 Locale 都是 CultureInfo，认不出的语言会退回中文
    private static void ApplyThemeLocale(CultureInfo culture)
    {
        if (Application.Current == null) return;

        foreach (IStyle style in Application.Current.Styles)
        {
            if (style is Semi.Avalonia.SemiTheme semi) semi.Locale = culture;
            else if (style is Ursa.Themes.Semi.UrsaSemiTheme ursa) ursa.Locale = culture;
        }
    }
}
