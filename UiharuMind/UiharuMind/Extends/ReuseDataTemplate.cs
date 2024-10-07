using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Metadata;

namespace UiharuMind.Extends;

public class ReuseDataTemplate : IRecyclingDataTemplate, ITypedDataTemplate
{
    private readonly Dictionary<object, Control> _cacheDictionary = new();

    [DataType] public Type? DataType { get; set; }

    [Content] [TemplateContent] public object? Content { get; set; }

    public bool Match(object? data)
    {
        if (DataType == null)
        {
            return true;
        }

        return DataType.IsInstanceOfType(data);
    }

    public Control Build(object? data) => Build(data, null);

    public Control Build(object? data, Control? existing)
    {
        return existing ?? FindControl(data);
    }

    private Control FindControl(object? data)
    {
        if (data == null) return new TextBlock() { Text = "Null Data" };
        if (_cacheDictionary.TryGetValue(data, out var template)) return template;
        var control = TemplateContent.Load(Content)?.Result;
        if (control == null) return new TextBlock() { Text = "Null Control" };
        _cacheDictionary[data] = control;
        return control;
    }
}