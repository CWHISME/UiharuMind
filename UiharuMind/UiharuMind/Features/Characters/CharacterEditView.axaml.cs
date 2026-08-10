using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UiharuMind.Features.Characters;

public partial class CharacterEditView : UserControl
{
    // public static readonly StyledProperty<CharacterInfoViewData> CharacterInfoProperty =
    //     AvaloniaProperty.Register<CharacterEditView, CharacterInfoViewData>(nameof(CharacterInfo));
    //
    // public CharacterInfoViewData CharacterInfo
    // {
    //     get => GetValue(CharacterInfoProperty);
    //     set => SetValue(CharacterInfoProperty, value);
    // }

    // protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    // {
    //     base.OnPropertyChanged(change);
    //     if (change.Property == CharacterInfoProperty)
    //     {
    //         var newInfo = change.GetNewValue<CharacterInfoViewData>();
    //         DataContext = newInfo;
    //     }
    // }

    public CharacterEditView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 展开前给片段选择器换一份数据。片段库可能刚被别处增删，且「存为片段」要取到
    /// <b>当前</b>提示词框里的文本，所以每次展开都重新绑一份。
    /// </summary>
    private void OnSnippetPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is not CharacterInfoViewData data) return;

        SnippetPicker.DataContext = new PromptSnippetPickerViewData(
            () => data.Template,
            text => data.InsertSnippet(text));
    }

    public void ScrollToSection(string? sectionKey)
    {
        Control? target = sectionKey switch
        {
            "Basic" => BasicInfoSection,
            "Function" => FunctionTypeSection,
            "Prompt" => PromptSection,
            "Greeting" => GreetingSection,
            "Dialog" => DialogTemplateSection,
            "Parameters" => ParametersSection,
            _ => BasicInfoSection
        };

        target.BringIntoView();
    }
}
