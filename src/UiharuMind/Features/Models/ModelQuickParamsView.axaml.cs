using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.Core;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models;

/// <summary>
/// 对话页模型栏的快捷参数：当前本地模型自己的运行参数（与模型列表里「运行参数」同一套行），全局设置走链接
/// </summary>
public partial class ModelQuickParamsView : UserControl
{
    public ModelQuickParamsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点了「全局设置」
    /// </summary>
    public event Action? GlobalSettingsRequested;

    /// <summary>
    /// 换成某个模型（每次弹出时调，模型可能已经换了）
    /// </summary>
    /// <param name="model">当前模型，没选为 null</param>
    public void Show(ModelRunningData? model)
    {
        bool local = model is { IsRemoteModel: false };
        ModelRuntimeOverridesViewData? data = local ? ModelRuntimeOverridesView.CreateData(model!) : null;
        ModelNameText.Text = model?.ModelName ?? Loc.Text(LangKey.ModelParamsNoModel);
        DescText.Text = Loc.Text(local ? LangKey.ModelParamsDesc
            : model == null ? LangKey.ModelParamsNoModelDesc : LangKey.ModelParamsRemoteDesc);
        Rows.DataContext = data;
        Rows.IsVisible = local;
        ResetAllButton.Command = data?.ResetAllCommand;
        ResetAllButton.IsVisible = local;
    }

    private void OnGlobalSettingsClick(object? sender, RoutedEventArgs e) => GlobalSettingsRequested?.Invoke();
}
