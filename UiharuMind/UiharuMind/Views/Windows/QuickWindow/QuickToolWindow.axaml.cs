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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character.Skills;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

/// <summary>
/// 当复制操作发生后，显示在复制位置的工具
/// </summary>
public partial class QuickToolWindow : QuickFloatingWindowBase
{
    public static void Show(string answerString)
    {
        UIManager.ShowWindow<QuickToolWindow>(x => x.SetAnswerString(answerString));
    }

    public QuickToolWindow()
    {
        InitializeComponent();

        UiAnimationUtils.PrepareRightToLeftTransitionTarget(MainMenu);
        LocalizationManager.Instance.LanguageChanged += InitFunctionMenu;
        InitFunctionMenu();
        // SubMenuComboBox.SelectionChanged += OnSubMenuComboBoxSelectionChanged;
    }

    private string? _answerString;

    public void SetAnswerString(string text)
    {
        _answerString = text;
        // Log.Debug("Set answer string: " + text);
    }

    private void OnMainButtonClock(object? sender, RoutedEventArgs e)
    {
        AssistantExplainAgentSkill skill = new AssistantExplainAgentSkill();
        QuickChatResultWindow.Show(Lang.Explain, _answerString, skill);
        PlayAnimation(false, SafeClose);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        //ignore
    }

    private void OnMainButtonPointerEntered(object? sender, PointerEventArgs e)
    {
        if (MainMenu.Opacity >= 1) return;
        PlayAnimation(true);
    }

    protected override void PlayAnimation(bool isShowed, Action? onCompleted = null)
    {
        UiAnimationUtils.PlayRightToLeftTransitionAnimation(MainMenu, isShowed, onCompleted);
    }

    private void InitFunctionMenu()
    {
        FunctionMenu.Children.Clear();
        AddFunctionMenu(nameof(Lang.Translation),
            () =>
            {
                TranslationAgentSkill skill = new TranslationAgentSkill();
                QuickChatResultWindow.Show(Lang.Translation, _answerString, skill);
            });
        AddFunctionMenu(nameof(Lang.SyntacticAnalysis), () =>
        {
            QuickChatResultWindow.Show(Lang.SyntacticAnalysis, _answerString, new AssistantSyntacticAnalysisAgentSkill());
        });
        AddFunctionMenu(nameof(Lang.Think), () =>
        {
            QuickChatResultWindow.Show(Lang.Think, _answerString, new ChainofThoughtAgentSkill());
        });
        AddFunctionMenu(nameof(Lang.Ask), () => { QuickStartChatWindow.Show(_answerString); });
    }

    private void AddFunctionMenu(string textKey, Action action, int xMargin = 5)
    {
        var btn = new Button
        {
            Content = LocalizationManager.Instance.GetString(textKey),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Command = new RelayCommand(action),
            Margin = new Thickness(xMargin, 0, 0, 0),
            MinHeight = 25,
        };
        FunctionMenu.Children.Add(btn);
    }
}
