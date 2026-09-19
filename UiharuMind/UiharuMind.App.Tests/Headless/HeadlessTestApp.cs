using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Semi.Avalonia;
[assembly: AvaloniaTestApplication(typeof(UiharuMind.App.Tests.Headless.HeadlessTestApp))]

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 无头测试用的应用实例。
///
/// <b>刻意不复用 <c>UiharuMind.App</c></b>：那一个的
/// <c>OnFrameworkInitializationCompleted</c> 会建托盘、建常驻窗口、挂全局键盘钩子、
/// 拉起整套 Core 管理器——测一个气泡的排版不该把半个应用叫醒，而且那些东西在 CI 上根本起不来。
///
/// 这里只搬<b>控件模板与资源</b>那一层，也就是 <c>App.axaml</c> 里
/// <c>Application.Styles</c> / <c>Application.Resources</c> 的等价物。
/// 两边要同步：那边加一份主题，这边不加，症状是控件在测试里长得跟应用里不一样，
/// 或者干脆抛 <c>KeyNotFoundException</c>。
/// </summary>
public sealed class HeadlessTestApp : Application
{
    public override void Initialize()
    {
        Resources["ControlContentThemeFontSize"] = 13d;
        Resources["ToolTipBorderThemeThickness"] = new Thickness(1);
        FontFamily mainFont = new("avares://UiharuMind/Assets/Fonts#HarmonyOS Sans");
        Resources["MainFont"] = mainFont;
        Resources["ContentControlThemeFontFamily"] = mainFont;
        Resources.MergedDictionaries.Add(
            new ResourceInclude((Uri?)null) { Source = new Uri("avares://LiveMarkdown.Avalonia/Defaults.axaml") });

        Styles.Add(Include("avares://Semi.Avalonia.ColorPicker/Index.axaml"));
        Styles.Add(Include("avares://UiharuMind/Assets/Themes/CustomButtonStyle.axaml"));
        Styles.Add(Include("avares://UiharuMind/Assets/Themes/CustomFontStyle.axaml"));
        Styles.Add(Include("avares://UiharuMind/Assets/Themes/CustomInputStyle.axaml"));
        Styles.Add(Include("avares://UiharuMind/Assets/Themes/CustomStatusStyle.axaml"));
        Styles.Add(Include("avares://UiharuMind/Assets/ThemeColors.axaml"));
        Styles.Add(new SemiTheme());
        Styles.Add(new Ursa.Themes.Semi.UrsaSemiTheme());
        Styles.Add(Include("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"));
        Styles.Add(Include("avares://LiveMarkdown.Avalonia/Styles.axaml"));
    }

    private static StyleInclude Include(string uri) =>
        new((Uri?)null) { Source = new Uri(uri) };

    /// <summary>
    /// 无头测试会话的入口（<c>[AvaloniaFact]</c> 按 <c>AvaloniaTestApplication</c> 找到它）。
    /// 渲染后端用 Avalonia 自带的无头实现，不碰 GPU 与窗口系统
    /// </summary>
    /// <returns>应用构建器</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
