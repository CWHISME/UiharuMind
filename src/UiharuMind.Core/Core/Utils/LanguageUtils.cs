/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Globalization;

namespace UiharuMind.Core.Core.Utils;

public static class LanguageUtils
{
    public static CultureInfo CurCultureInfo = CultureInfo.CurrentCulture;

    public const string EnglishUnitedStates = "en-US";
    public const string ChineseSimplified = "zh-CN";

    public static CultureInfo[] SupportedLanguages =
    {
        new CultureInfo(EnglishUnitedStates),
        new CultureInfo(ChineseSimplified)
    };

    public static CultureInfo GetSupportedCultureOrDefault(string? languageCode)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            foreach (var cultureInfo in SupportedLanguages)
            {
                if (string.Equals(cultureInfo.Name, languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    return cultureInfo;
                }
            }
        }

        foreach (var cultureInfo in SupportedLanguages)
        {
            if (string.Equals(cultureInfo.Name, CultureInfo.CurrentCulture.Name, StringComparison.OrdinalIgnoreCase))
            {
                return cultureInfo;
            }
        }

        foreach (var cultureInfo in SupportedLanguages)
        {
            if (string.Equals(cultureInfo.TwoLetterISOLanguageName, CultureInfo.CurrentCulture.TwoLetterISOLanguageName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return cultureInfo;
            }
        }

        return new CultureInfo(EnglishUnitedStates);
    }
}
