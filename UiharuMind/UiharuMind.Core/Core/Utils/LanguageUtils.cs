using System.Globalization;

namespace UiharuMind.Core.Core.Utils;

public static class LanguageUtils
{
    public const string EnglishUnitedStates = "en-US";
    public const string ChineseSimplified = "zh-CN";

    public static CultureInfo[] SupportedLanguages =
    {
        new CultureInfo(EnglishUnitedStates),
        new CultureInfo(ChineseSimplified)
    };
}