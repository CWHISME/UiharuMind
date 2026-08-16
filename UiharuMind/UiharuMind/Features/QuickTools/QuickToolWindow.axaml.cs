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
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.QuickChat;

namespace UiharuMind.Features.QuickTools;

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
        AssistantExplainPromptAction skill = new AssistantExplainPromptAction();
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
                TranslationPromptAction skill = new TranslationPromptAction();
                QuickChatResultWindow.Show(Lang.Translation, _answerString, skill);
            });
        AddFunctionMenu(nameof(Lang.SyntacticAnalysis), () => { QuickChatResultWindow.Show(Lang.SyntacticAnalysis, _answerString, new AssistantSyntacticAnalysisPromptAction()); });
        AddFunctionMenu(nameof(Lang.Think), () => { QuickChatResultWindow.Show(Lang.Think, _answerString, new ChainOfThoughtPromptAction()); });
        AddFunctionMenu(nameof(Lang.Ask), () => { QuickStartChatWindow.Show(_answerString); });
    }

    private void AddFunctionMenu(string textKey, Action action, int xMargin = 5)
    {
        var btn = new Button
        {
            Content = LocalizationManager.Instance.GetString(textKey),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Command = new RelayCommand(() =>
            {
                action();
                SafeClose();
            }),
            Margin = new Thickness(xMargin, 0, 0, 0),
            MinHeight = 25,
        };
        FunctionMenu.Children.Add(btn);
    }
}