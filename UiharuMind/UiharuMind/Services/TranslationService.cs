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

// using System.Globalization;
// using System.Resources;
// using UiharuMind.Resources.Lang;
//
// namespace UiharuMind.Services;
//
// public class TranslationService
// {
//     private readonly ResourceManager _resourceManager;
//     private readonly CultureInfo _cultureInfo;
//
//     public TranslationService(string baseName, CultureInfo cultureInfo)
//     {
//         _resourceManager = new ResourceManager(baseName, typeof(TranslationService).Assembly);
//         _cultureInfo = cultureInfo;
//     }
//
//     public string GetString(string key)
//     {
//         // return _resourceManager.GetString(key, _cultureInfo) ?? key;
//         return Lang.ResourceManager.GetString()
//     }
// }