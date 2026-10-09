using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using UiharuMind.Generated;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Markup;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// 一种本地引擎设置页的文案
/// </summary>
/// <param name="EngineTitle">引擎卡节标题</param>
/// <param name="EngineDesc">引擎卡节说明</param>
/// <param name="ModelsTitle">模型入口节标题</param>
/// <param name="ModelsDesc">模型入口节说明</param>
/// <param name="ManageModels">入口行标题</param>
/// <param name="ManageModelsDesc">入口行说明</param>
/// <param name="ManageModelsButton">入口按钮</param>
/// <param name="RuntimeTitle">运行参数节标题</param>
/// <param name="RuntimeDesc">运行参数节说明</param>
public sealed record LocalEngineSettingTexts(
    LangKey EngineTitle,
    LangKey EngineDesc,
    LangKey ModelsTitle,
    LangKey ModelsDesc,
    LangKey ManageModels,
    LangKey ManageModelsDesc,
    LangKey ManageModelsButton,
    LangKey RuntimeTitle,
    LangKey RuntimeDesc);

/// <summary>
/// 本地模型（语音 / 生图）设置页的骨架：引擎卡 + 去模型页管模型的入口 + 运行参数。
/// 各引擎的页继承它，只给文案、运行参数那几行和「管理模型」跳到哪
/// </summary>
public partial class LocalEngineSettingView : UserControl
{
    private readonly Action _manageModels = () => { };

    /// <summary>
    /// 只供 XAML 加载器用
    /// </summary>
    public LocalEngineSettingView()
    {
        InitializeComponent();
    }

    /// <param name="texts">文案</param>
    /// <param name="scrollKey">滚动记忆 key（页标题 key）</param>
    /// <param name="engine">引擎卡</param>
    /// <param name="runtime">运行参数那几行</param>
    /// <param name="manageModels">「管理模型」：跳到模型页对应页签</param>
    protected LocalEngineSettingView(LocalEngineSettingTexts texts, string scrollKey, EngineVersionsViewData engine,
        Control runtime, Action manageModels) : this()
    {
        _manageModels = manageModels;
        SettingsScrollMemory.SetScrollKey(Scroller, scrollKey);
        EngineCard.DataContext = engine;
        RuntimeSection.Body = runtime;

        BindText(EngineSection, SettingsSection.HeaderProperty, texts.EngineTitle);
        BindText(EngineSection, SettingsSection.DescriptionProperty, texts.EngineDesc);
        BindText(ModelsSection, SettingsSection.HeaderProperty, texts.ModelsTitle);
        BindText(ModelsSection, SettingsSection.DescriptionProperty, texts.ModelsDesc);
        BindText(ManageModelsRow, SettingsRow.HeaderProperty, texts.ManageModels);
        BindText(ManageModelsRow, SettingsRow.DescriptionProperty, texts.ManageModelsDesc);
        BindText(ManageModelsButton, ContentProperty, texts.ManageModelsButton);
        BindText(RuntimeSection, SettingsSection.HeaderProperty, texts.RuntimeTitle);
        BindText(RuntimeSection, SettingsSection.DescriptionProperty, texts.RuntimeDesc);
    }

    // 与 {loc:Loc} 同一种绑定：换语言跟着变，不依赖 DataContext（设置搜索在页面没显示时也读得到）
    private static void BindText(AvaloniaObject target, AvaloniaProperty property, LangKey key) =>
        target.Bind(property, new Binding
        {
            Source = LocalizedString.Get(key.ToString()),
            Path = nameof(LocalizedString.Value)
        });

    private void OnManageModelsClick(object? sender, RoutedEventArgs e) => _manageModels();
}
