using System;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace UiharuMind.Shared.Markup;

/// <summary>
/// XAML 里取本地化文案的 markup extension，用法 <c>{loc:Loc Key}</c> 或
/// <c>{loc:Loc Key, SettingProperty=SomeSetting}</c>（后者在文案后拼设置值）。
/// </summary>
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
        // 用 IObservable<string>.ToBinding() 而非“Source=对象 + Path=属性”的反射绑定：
        // 走 Avalonia 的 observable 绑定通道，裁剪/AOT 下没有反射点。
        return LocalizedString.Get(_key, SettingProperty).ToBinding();
    }
}
