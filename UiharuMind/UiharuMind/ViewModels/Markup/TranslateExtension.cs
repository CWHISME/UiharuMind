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