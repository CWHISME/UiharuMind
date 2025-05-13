using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character.Skills;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Utils;
using UiharuMind.ViewModels.UIHolder;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class QuickTranslationWindow : QuickWindowBase
{
    public QuickTranslationWindow()
    {
        InitializeComponent();

        DataContext = this;

        _agentSkill = new TranslationAdvancedAgentSkill();
        _simpleAgentSkill = new TranslationAgentSkill();
        _autoScrollHolder = new ScrollViewerAutoScrollHolder(ScrollViewer);
    }

    public override void Awake()
    {
        base.Awake();
        this.CanResize = true;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        IsFinished = true;

        var cultures = CultureInfo.GetCultures(CultureTypes.NeutralCultures);
        _languages.Add(Lang.AutoDetect);
        foreach (var culture in cultures)
        {
            if (string.IsNullOrEmpty(culture.Name)) continue;
            if (culture.Name.Equals("zh") || culture.Name.Equals("en"))
                _commonlyUsedLanguages.Add(culture.DisplayName);
            _languages.Add(culture.DisplayName);
        }

        TargetLanguageComboBox.ItemsSource = _languages;
        TargetLanguageComboBox.SelectedIndex = 0;
        TargetLanguageComboBox.SelectionChanged += OnTargetLanguageComboBoxSelectionChanged;

        CommonlyUsedLanguagePanel.Children.Clear();
        foreach (var language in _commonlyUsedLanguages)
        {
            var button = new ToggleButton();
            button.Content = language;
            button.IsChecked = false;
            button.Click += CommonlyUsedLanguageButton_Click;
            CommonlyUsedLanguagePanel.Children.Add(button);
        }
    }

    private readonly ScrollViewerAutoScrollHolder _autoScrollHolder;

    private CancellationTokenSource? _cts;

    private List<string> _languages = new List<string>();
    private List<string> _commonlyUsedLanguages = new List<string>();

    public bool IsFinished
    {
        get => !InAnswerPanel.IsVisible;
        private set
        {
            InAnswerPanel.IsVisible = !value;
            LoadingEffect.IsLoading = !value;
            RegenerateButton.IsVisible = value;
        }
    }

    // private string _askContent;
    private readonly TranslationAgentSkill _simpleAgentSkill;
    private readonly TranslationAdvancedAgentSkill _agentSkill;

    private AgentSkillBase GetSkill()
    {
        if(string.IsNullOrEmpty(ExtraRequestTextBox.Text)) return _simpleAgentSkill;
        _agentSkill.SetExtraRequest(ExtraRequestTextBox.Text);
        return _agentSkill;
    }

    public void SetRequestInfo(string? content, AgentSkillBase agentSkill)
    {
        _cts.SafeStop();
        if (string.IsNullOrEmpty(content))
        {
            Log.Error("Plase input content!");
            return;
        }

        if (TargetLanguageComboBox.SelectedIndex > 0)
            agentSkill.SetLanguage(TargetLanguageComboBox.SelectedItem?.ToString()!);
        else agentSkill.RemoveLanguage();
        // agentSkill.SetExtraRequest(string.IsNullOrEmpty(ExtraRequestTextBox.Text) ? "None" : ExtraRequestTextBox.Text);
        // _askContent = content;
        // _agentSkill = agentSkill;

        _cts = new CancellationTokenSource();
        IsFinished = false;

        async void Action()
        {
            try
            {
                await foreach (var message in agentSkill.DoSkill(LlmManager.Instance.CurrentRunningModel!, content,
                                   _cts.Token))
                {
                    AppendContent(message);
                }
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);
            }

            IsFinished = true;
        }

        Dispatcher.UIThread.Post(Action, DispatcherPriority.ApplicationIdle);
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        this.SetWindowToMousePosition(HorizontalAlignment.Right, VerticalAlignment.Center);
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        _cts.SafeStop();
    }

    private void AppendContent(string info)
    {
        ResultTextBlock.Text = info;
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    private void OnStopButtonClick(object? sender, RoutedEventArgs e)
    {
        _cts.SafeStop();
        IsFinished = true;
    }

    private void OnRegenerateButtonClick(object? sender, RoutedEventArgs e)
    {
        SetRequestInfo(InputBox.Text, GetSkill());
    }

    //=======常用语言快捷切换=======
    private void OnTargetLanguageComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshCommonlyUsedLanguagesState();
    }

    private void CommonlyUsedLanguageButton_Click(object? sender, RoutedEventArgs e)
    {
        TargetLanguageComboBox.SelectedItem = (sender as ToggleButton)?.Content;
        RefreshCommonlyUsedLanguagesState();
    }

    private void RefreshCommonlyUsedLanguagesState()
    {
        foreach (var contrl in CommonlyUsedLanguagePanel.Children)
        {
            var btn = contrl as ToggleButton;
            if (btn == null) continue;
            btn.IsChecked = (string?)btn.Content == TargetLanguageComboBox.SelectedItem?.ToString();
        }
    }
    //=========================

    [RelayCommand]
    public void SendMessage()
    {
        SetRequestInfo(InputBox.Text, GetSkill());
    }
}