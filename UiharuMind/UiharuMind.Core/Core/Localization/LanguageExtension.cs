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

namespace UiharuMind.Core.Core.Localization
{
    public static class LanguageExtension
    {
        public static string Translate(this string str)
        {
            return LanguageManager.Instance.Get(str);
        }

        public static string Translate(this string str, object param1)
        {
            return LanguageManager.Instance.Get(str, param1);
        }

        public static string Translate(this string str, object param1, object param2)
        {
            return LanguageManager.Instance.Get(str, param1, param2);
        }

        public static string Translate(this string str, object param1, object param2, object param3)
        {
            return LanguageManager.Instance.Get(str, param1, param2, param3);
        }

        public static string Translate(this string str, params object[] param)
        {
            return LanguageManager.Instance.Get(str, param);
        }
    }
}