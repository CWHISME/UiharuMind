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

using System;
using Avalonia.Markup.Xaml;
using UiharuMind.Resources.Lang;

namespace UiharuMind.ViewModels.Markup;

public class TranslateExtension : MarkupExtension
{
    private readonly string _key;

    public TranslateExtension(string key)
    {
        _key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Lang.ResourceManager.GetString(_key) ?? "NotFound: " + _key; //App.TranslationService.GetString(_key);
    }
}