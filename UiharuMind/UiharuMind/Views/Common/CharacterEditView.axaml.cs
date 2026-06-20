using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

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

    public void ScrollToSection(string? sectionKey)
    {
        Control? target = sectionKey switch
        {
            "Basic" => BasicInfoSection,
            "Function" => FunctionTypeSection,
            "Mounts" => MountsSection,
            "Prompt" => PromptSection,
            "Greeting" => GreetingSection,
            "Dialog" => DialogTemplateSection,
            "Parameters" => ParametersSection,
            _ => BasicInfoSection
        };

        target.BringIntoView();
    }
}
