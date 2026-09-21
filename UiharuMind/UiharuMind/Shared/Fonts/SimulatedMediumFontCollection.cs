using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;

namespace UiharuMind.Shared.Fonts;

/// <summary>
/// 程序化加粗：把 HarmonyOS Sans SC 家族的 Medium(500)/SemiBold(600) 请求映射到
/// Regular 字面 + <see cref="FontSimulations.Bold"/>（Skia fake bold），
/// Bold(700) 仍命中真 Bold 字面。
///
/// 动机：MiSans 因授权禁止修改不可用；HarmonyOS 官方没有 SemiBold、Medium 字面要额外
/// 背 8MB 体积。这里用 Avalonia 内建的合成加粗（与「只有 Regular 时 Bold 自动加粗」
/// 同一机制）在字重语义层做映射，XAML 里照常写 <c>FontWeight="Medium"</c>。
/// </summary>
public sealed class SimulatedMediumFontCollection : FontCollectionBase
{
    private const string TargetFamilyName = "HarmonyOS Sans SC";

    /// <summary>注册到 FontManager 的集合 key（AddFontCollection 只收 fonts: scheme）。</summary>
    private static readonly Uri MainFontsKey = new("fonts:UiharuMind/Assets/Fonts");

    /// <summary>字体资产源：仍从 avares 资源加载（FontFamilyLoader 只认 avares/resm）。</summary>
    private static readonly Uri MainFontsSource = new("avares://UiharuMind/Assets/Fonts");

    private static SimulatedMediumFontCollection? _registered;

    private readonly Uri _key;

    /// <summary>请求 key → 合成的 Regular+假粗体 GlyphTypeface。</summary>
    private readonly ConcurrentDictionary<FontCollectionKey, GlyphTypeface> _syntheticCache = new();

    private static IFontManagerImpl? _fontManagerImpl;
    private static MethodInfo? _tryCreateGlyphTypefaceMethod;

    /// <summary>
    /// 拿字体平台的实现。<c>FontManager.PlatformImpl</c> 在 Avalonia 12.1.2 里是 internal
    /// （AvaloniaLocator.Current 同样是 internal），只能反射取一次并缓存。
    /// 值本身是 Skia 的 FontManagerImpl，其 <c>TryCreateGlyphTypeface(stream, simulations)</c>
    /// 会返回带 FontSimulations 的真 SkiaTypeface——渲染端（GlyphRunImpl）强转 SkiaTypeface，
    /// 所以这一步不能省、也不能换成自定义 IPlatformTypeface。
    /// </summary>
    private static IFontManagerImpl FontManagerImpl
    {
        get
        {
            if (_fontManagerImpl is null)
            {
                var property = typeof(FontManager).GetProperty(
                    "PlatformImpl",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                _fontManagerImpl = property?.GetValue(FontManager.Current) as IFontManagerImpl
                    ?? throw new InvalidOperationException("Unable to resolve IFontManagerImpl (FontManager.PlatformImpl).");
            }

            return _fontManagerImpl;
        }
    }

    public SimulatedMediumFontCollection(Uri key, Uri source)
    {
        _key = key;
        TryAddFontSource(source);
    }

    public override Uri Key => _key;

    /// <summary>
    /// 应用启动时调用一次，用本集合替换 FontFamilyLoader 自动创建的 EmbeddedFontCollection
    /// （<see cref="FontManager.AddFontCollection"/> 对同 key 是替换语义）。
    /// </summary>
    public static void Register()
    {
        if (_registered is not null)
        {
            return;
        }

        _registered = new SimulatedMediumFontCollection(MainFontsKey, MainFontsSource);
        FontManager.Current.AddFontCollection(_registered);
    }

    public override bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight,
        FontStretch stretch, [NotNullWhen(true)] out GlyphTypeface? glyphTypeface)
    {
        if ((weight == FontWeight.Medium || weight == FontWeight.SemiBold) &&
            string.Equals(familyName, TargetFamilyName, StringComparison.OrdinalIgnoreCase))
        {
            var key = new FontCollectionKey(style, weight, stretch);

            if (_syntheticCache.TryGetValue(key, out var cached) && cached is not null)
            {
                glyphTypeface = cached;
                return true;
            }

            // 拿 Regular 字面作为合成底子
            if (base.TryGetGlyphTypeface(familyName, style, FontWeight.Normal, stretch, out var regular) &&
                regular is not null)
            {
                var simulations = FontSimulations.Bold;

                if (style != FontStyle.Normal && regular.Style != style)
                {
                    simulations |= FontSimulations.Oblique;
                }

                // 与 FontCollectionBase.TryCreateSyntheticGlyphTypeface 同一路径：
                // 用 IFontManagerImpl 从流重建带 FontSimulations 的 platform typeface，
                // 这样 Skia 渲染时 Embolden 才会真正生效。
                if (regular.PlatformTypeface.TryGetStream(out var stream))
                {
                    using (stream)
                    {
                        if (TryCreateGlyphTypefaceViaReflection(stream, simulations, out var platformTypeface))
                        {
                            glyphTypeface = new GlyphTypeface(platformTypeface, simulations);
                            _syntheticCache[key] = glyphTypeface;
                            return true;
                        }
                    }
                }
            }
        }

        return base.TryGetGlyphTypeface(familyName, style, weight, stretch, out glyphTypeface);
    }

    /// <summary>
    /// Avalonia 12.1.x 里 <see cref="IFontManagerImpl"/> 的成员是 internal（接口公开但成员藏起来），
    /// 编译期调不到，只能反射。MethodInfo 静态缓存：字体合成本身有 <see cref="_syntheticCache"/>，
    /// 每种请求只重建一次，这里再把元数据查找也省掉，避免任何重复反射开销。
    /// </summary>
    private static bool TryCreateGlyphTypefaceViaReflection(
        Stream stream, FontSimulations simulations, out IPlatformTypeface? platformTypeface)
    {
        _tryCreateGlyphTypefaceMethod ??= typeof(IFontManagerImpl).GetMethod(
            "TryCreateGlyphTypeface",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(Stream), typeof(FontSimulations), typeof(IPlatformTypeface).MakeByRefType()],
            modifiers: null);

        if (_tryCreateGlyphTypefaceMethod is null)
        {
            throw new InvalidOperationException("IFontManagerImpl.TryCreateGlyphTypeface(Stream,...) not found via reflection.");
        }

        var args = new object?[] { stream, simulations, null };
        var ok = (bool)_tryCreateGlyphTypefaceMethod.Invoke(FontManagerImpl, args)!;
        platformTypeface = (IPlatformTypeface?)args[2];
        return ok;
    }
}
