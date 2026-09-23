using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Markup;

/// <summary>
/// 一条本地化文案的可观察流：订阅时立即推当前值（BehaviorSubject 语义），
/// 语言或相关设置变化时再推新值。供 <see cref="LocExtension"/> 以
/// <c>IObservable&lt;string&gt;.ToBinding()</c> 消费。
/// </summary>
public class LocalizedString : IObservable<string>
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
    private readonly object _gate = new();
    private readonly List<IObserver<string>> _observers = new();

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
    /// 取（并缓存）一个 key 的本地化流。缓存与 LocalizationManager/Setting 同为单例，
    /// 因此应用生命周期内有效，控件销毁只退订、不销毁流本身。
    /// </summary>
    /// <param name="key">资源键</param>
    /// <param name="settingProperty">要拼到文案后的设置属性名；可为 null</param>
    /// <returns>本地化流</returns>
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

    public IDisposable Subscribe(IObserver<string> observer)
    {
        lock (_gate)
        {
            _observers.Add(observer);
        }

        Notify(observer, Value);
        return new Subscription(this, observer);
    }

    private string? GetSettingValue()
    {
        return _settingGetter?.Invoke(ConfigManager.Instance.Setting);
    }

    private void OnLanguageChanged()
    {
        Push();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != _settingProperty) return;
        Push();
    }

    private void Push()
    {
        var value = Value;
        IObserver<string>[] snapshot;
        lock (_gate)
        {
            snapshot = _observers.ToArray();
        }

        foreach (var observer in snapshot)
        {
            Notify(observer, value);
        }
    }

    // Avalonia 的 observable 绑定不自动封送线程（UntypedObservableBindingExpression.OnNext
    // 直接 PublishValue），跨线程推值必须回到 UI 线程，否则撞控件线程亲和性。
    private static void Notify(IObserver<string> observer, string value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            observer.OnNext(value);
        }
        else
        {
            Dispatcher.UIThread.Post(() => observer.OnNext(value));
        }
    }

    private void Unsubscribe(IObserver<string> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly LocalizedString _owner;
        private readonly IObserver<string> _observer;

        public Subscription(LocalizedString owner, IObserver<string> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose()
        {
            _owner.Unsubscribe(_observer);
        }
    }
}
