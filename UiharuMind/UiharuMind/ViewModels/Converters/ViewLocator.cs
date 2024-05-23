using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using UiharuMind.ViewModels.Interfaces;

namespace UiharuMind.ViewModels.Converters;

/// <summary>
/// Control 制造者，要求目标实现 IViewControl
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is IViewControl vc) return vc.View;
        if (param is null) return null;
        return new TextBlock { Text = "ViewControl Not Found : " + param.GetType().Name };
    }

    public bool Match(object? data)
    {
        return true;
    }
}