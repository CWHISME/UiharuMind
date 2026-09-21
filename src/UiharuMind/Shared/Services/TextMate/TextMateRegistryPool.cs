/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace UiharuMind.Shared.Services.TextMate;

/// <summary>
/// 全进程共享的 TextMate 注册表。<b>任何时候都不要自己 new Registry</b>。
///
/// <para>
/// <see cref="Registry"/> 把用到的每一份 raw 语法树（<c>SyncRegistry._rawGrammars</c>）
/// 和编译后的规则表（<c>_grammars</c>）整套挂在自己身上，而且一份语法会连带拉进它
/// include 的所有 scope。按控件实例各 new 一个，就是把整套语法原样复制几份——
/// 实测堆里同时活着 4~5 份时，光 raw 语法树就占 16MB。
/// </para>
///
/// <para>
/// 主题切换不重建注册表，只在同一份上 <c>SetTheme</c>：编译好的语法与已 tokenize 的
/// scopes 全部留着，只重匹配颜色。<see cref="RegistryOptions"/> 同理只建一次——
/// 构造它要扫一遍全部语法的 package.json，而它的主题参数只是<b>默认</b>主题，
/// 换主题走 <c>LoadTheme</c> 即可，不必重建。
/// </para>
/// </summary>
public static class TextMateRegistryPool
{
    private static RegistryOptions? _options;
    private static Registry? _registry;
    private static ThemeName _appliedTheme;

    /// <summary>当前应该用的主题。跟着应用的明暗走</summary>
    public static ThemeName CurrentTheme =>
        ApplicationThemeManager.IsDarkTheme() ? ThemeName.DarkPlus : ThemeName.LightPlus;

    /// <summary>语法与主题表。查语言、查 scope 都走这里</summary>
    public static RegistryOptions Options
    {
        get
        {
            EnsureUpToDate();
            return _options!;
        }
    }

    /// <summary>全进程唯一的注册表。取用时主题已是最新的</summary>
    public static Registry Registry
    {
        get
        {
            EnsureUpToDate();
            return _registry!;
        }
    }

    // 只在 UI 线程被调用（TextMate 这套本身不是线程安全的，调用方也都在 UI 线程上）
    private static void EnsureUpToDate()
    {
        ThemeName theme = CurrentTheme;

        if (_options == null)
        {
            _options = new RegistryOptions(theme);
            _registry = new Registry(_options);
            _appliedTheme = theme;
            return;
        }

        if (_appliedTheme == theme) return;

        _registry!.SetTheme(_options.LoadTheme(theme));
        _appliedTheme = theme;
    }
}
