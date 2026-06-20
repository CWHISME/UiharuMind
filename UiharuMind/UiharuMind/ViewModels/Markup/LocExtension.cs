using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Configs;
using UiharuMind.Services;

namespace UiharuMind.ViewModels.Markup;

public class LocExtension : MarkupExtension
{
    private readonly string _key;
    public string? SettingProperty { get; set; }

    public LocExtension(string key)
    {
        _key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding(nameof(LocalizedString.Value))
        {
            Source = LocalizedString.Get(_key, SettingProperty),
            Mode = BindingMode.OneWay
        };
    }
}

public class LocalizedString : INotifyPropertyChanged
{
    private static readonly Dictionary<string, LocalizedString> Cache = new();

    private readonly string _key;
    private readonly string? _settingProperty;
    private readonly PropertyInfo? _settingPropertyInfo;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value
    {
        get
        {
            var value = LocalizationManager.Instance.GetString(_key);
            var settingValue = GetSettingValue();
            return string.IsNullOrWhiteSpace(settingValue) ? value : $"{value} ({settingValue})";
        }
    }

    private LocalizedString(string key, string? settingProperty)
    {
        _key = key;
        _settingProperty = settingProperty;
        if (!string.IsNullOrWhiteSpace(settingProperty))
        {
            _settingPropertyInfo = ConfigManager.Instance.Setting.GetType().GetProperty(settingProperty);
            ConfigManager.Instance.Setting.PropertyChanged += OnSettingChanged;
        }

        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    public static LocalizedString Get(string key, string? settingProperty = null)
    {
        var cacheKey = string.IsNullOrWhiteSpace(settingProperty) ? key : $"{key}:{settingProperty}";
        if (Cache.TryGetValue(cacheKey, out var localizedString))
        {
            return localizedString;
        }

        localizedString = new LocalizedString(key, settingProperty);
        Cache[cacheKey] = localizedString;
        return localizedString;
    }

    private string? GetSettingValue()
    {
        if (_settingPropertyInfo == null) return null;
        return _settingPropertyInfo.GetValue(ConfigManager.Instance.Setting) as string;
    }

    private void OnLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != _settingProperty) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
