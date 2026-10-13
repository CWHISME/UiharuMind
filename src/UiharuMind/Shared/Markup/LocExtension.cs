using System;
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
        // 绑定而非 IObservable.ToBinding()：死订阅条目在下次语言/设置变化推值时压缩
        //（Avalonia 不在控件回收时自动解绑，清理时机与旧 IObservable 路径相同）。
        // 实测（P2）订阅持续增长的主因是产物面板每次刷新重建容器，由增量更新解决。
        return LocalizedString.Get(_key, SettingProperty).CreateBinding();
    }
}
