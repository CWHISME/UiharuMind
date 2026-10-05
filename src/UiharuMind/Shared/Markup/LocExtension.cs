using System;
using Avalonia.Data;
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
        // 返回 Source=LocalizedString + Path=Value 的普通绑定而非 IObservable.ToBinding()：
        // 走 INPC 弱事件，控件回收不被源强持有；死订阅条目在下次语言/设置变化推值时压缩
        //（Avalonia 不在控件回收时自动解绑，清理时机与旧 IObservable 路径相同）。
        // 实测（P2）订阅持续增长的主因是产物面板每次刷新重建容器，由增量更新解决。
        // 代价是 Path= 走属性反射；主应用未开 AOT publish，与仓库里其它常规绑定一致。
        return new Binding
        {
            Source = LocalizedString.Get(_key, SettingProperty),
            Path = nameof(LocalizedString.Value),
        };
    }
}
