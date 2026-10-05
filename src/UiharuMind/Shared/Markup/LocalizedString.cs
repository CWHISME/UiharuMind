using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Markup;

/// <summary>
/// 一条本地化文案的可观察值：订阅方（Avalonia 绑定）通过 <see cref="INotifyPropertyChanged"/>
/// 在语言或相关设置变化时收到 <see cref="Value"/> 变更。供 <see cref="LocExtension"/> 以
/// <c>Binding Source=LocalizedString Path=Value</c> 消费。
/// 走 INPC 弱事件而非自管的 IObservable 订阅表：弱事件不保留已死控件（控件可正常回收），
/// 但死订阅条目与旧实现一样只在源推值（语言/设置变化）时压缩——Avalonia 不在控件回收时自动解绑。
/// 实测（P2）订阅持续增长的主因是产物面板每次刷新重建容器，已由增量更新解决；这里只是把订阅表交还 Avalonia。
/// </summary>
public class LocalizedString : INotifyPropertyChanged
{
    private static readonly Dictionary<string, LocalizedString> Cache = new();

    // XAML 里 SettingProperty 是字符串，用映射表把属性名收敛到强类型 getter，
    // 避免 GetProperty/GetValue 反射（AOT/裁剪友好）。新增可用的设置属性时在这里补一行。
    private static readonly Dictionary<string, Func<SettingConfig, string>> SettingGetters = new()
    {
        ["CaptureScreenShortcut"] = s => s.CaptureScreenShortcut,
        ["QuickStartChatShortcut"] = s => s.QuickStartChatShortcut,
        ["ClipboardHistoryShortcut"] = s => s.ClipboardHistoryShortcut,
        ["QuickTranslationShortcut"] = s => s.QuickTranslationShortcut,
        ["QuickAutoClickShortcut"] = s => s.QuickAutoClickShortcut,
    };

    private readonly string _key;
    private readonly string? _settingProperty;
    private readonly Func<SettingConfig, string>? _settingGetter;

    /// <summary>
    /// 当前文案；带设置值时拼成「文案 (设置值)」
    /// </summary>
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
            SettingGetters.TryGetValue(settingProperty, out _settingGetter);
            ConfigManager.Instance.Setting.PropertyChanged += OnSettingChanged;
        }

        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// 取（并缓存）一个 key 的本地化值。缓存与 LocalizationManager/Setting 同为单例，
    /// 因此应用生命周期内有效，控件销毁只解除绑定、不销毁值本身。
    /// </summary>
    /// <param name="key">资源键</param>
    /// <param name="settingProperty">要拼到文案后的设置属性名；可为 null</param>
    /// <returns>本地化值</returns>
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private string? GetSettingValue()
    {
        return _settingGetter?.Invoke(ConfigManager.Instance.Setting);
    }

    private void OnLanguageChanged()
    {
        RaiseValueChanged();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != _settingProperty) return;
        RaiseValueChanged();
    }

    // 绑定表达式不自动封送线程，跨线程推值必须回到 UI 线程，否则撞控件线程亲和性。
    private void RaiseValueChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
        else
        {
            Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))));
        }
    }
}