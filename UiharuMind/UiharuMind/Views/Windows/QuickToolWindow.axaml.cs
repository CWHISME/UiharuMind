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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;
using SharpHook.Native;
using UiharuMind.Core.AI.Character.Skills;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Resources.Lang;
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

        InitFunctionMenu();
        // SubMenuComboBox.SelectionChanged += OnSubMenuComboBoxSelectionChanged;
    }

    // public override void Awake()
    // {
    //     SizeToContent = SizeToContent.WidthAndHeight;
    //     this.SetSimpledecorationPureWindow();
    //     this.CanResize = true;
    // }

    // private void OnSubMenuComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    // {
    //     // CloseSelf();
    //     if (sender == null) return;
    //     var comboBox = (ComboBox)sender;
    //     var newItem = comboBox.SelectedItem;
    //     if (newItem != null)
    //     {
    //         comboBox.SelectedItem = null;
    //         PlayAnimation(false, SafeClose);
    //     }
    //
    //     // e.Handled = true;
    // }

    private string? _answerString;

    // protected override void OnPreShow()
    // {
    //     base.OnPreShow();
    //     InputManager.Instance.EventOnKeyDown += OnKeyDown;
    //     InputManager.Instance.EventOnMouseWheel += OnMouseWheel;
    //     BindMouseClickCloseEvent();
    //
    //     ShowActivated = false;
    //     this.SetWindowToMousePosition(HorizontalAlignment.Right, offsetX: 10, offsetY: -15);
    // }

    // protected override void OnPreClose()
    // {
    //     base.OnPreClose();
    //     InputManager.Instance.EventOnKeyDown -= OnKeyDown;
    //     InputManager.Instance.EventOnMouseWheel -= OnMouseWheel;
    // }

    public void SetAnswerString(string text)
    {
        _answerString = text;
        // Log.Debug("Set answer string: " + text);
    }

    private void OnMainButtonClock(object? sender, RoutedEventArgs e)
    {
        // UIManager.ShowWindow<QuickChatResultWindow>(x => x.SetRequestInfo(_answerString), null, true);
        // QuickStartChatWindow.Show(_answerString);
        // QuickChatResultWindow.Show("解释", _answerString, ConfigManager.Instance.QuickToolPromptSetting.Explanation);
        AssistantExplainAgentSkill skill = new AssistantExplainAgentSkill();
        // skill.SetLangate(Lang.Culture.EnglishName);
        QuickChatResultWindow.Show(Lang.Explain, _answerString, skill);
        PlayAnimation(false, SafeClose);
    }


    // private void OnMouseWheel(MouseWheelEventData obj)
    // {
    //     SafeClose();
    // }
    //
    // private void OnKeyDown(KeyCode obj)
    // {
    //     SafeClose();
    // }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        if (MainMenu.IsVisible) return;
        PlayAnimation(true);
    }

    // protected override void OnPointerExited(PointerEventArgs e)
    // {
    //     // if (SubMenuComboBox.IsDropDownOpen) return;
    //     PlayAnimation(false);
    // }
    //
    // private void PlayAnimation(bool isShowed, Action? onCompleted = null)
    // {
    //     UiAnimationUtils.PlayRightToLeftTransitionAnimation(MainMenu, isShowed, onCompleted);
    // }

    // protected override void SetWindowPosition()
    // {
    //     this.SetWindowToMousePosition(HorizontalAlignment.Center, offsetX: 0, offsetY: -15);
    // }

    protected override void PlayAnimation(bool isShowed, Action? onCompleted = null)
    {
        UiAnimationUtils.PlayRightToLeftTransitionAnimation(MainMenu, isShowed, onCompleted);
    }

    private void InitFunctionMenu()
    {
        FunctionMenu.Children.Clear();
        AddFunctionMenu(Lang.Translation,
            () =>
            {
                // TeamTranslationAgentSkill skill = new TeamTranslationAgentSkill();
                TranslationAgentSkill skill = new TranslationAgentSkill();
                // skill.AddParams("lang", Lang.Culture.EnglishName);
                // QuickChatResultWindow.Show("翻译", _answerString,
                //     ConfigManager.Instance.QuickToolPromptSetting.Translation);
                QuickChatResultWindow.Show(Lang.Translation, _answerString, skill);
            });
        AddFunctionMenu(Lang.Think,
            () => { QuickChatResultWindow.Show(Lang.Think, _answerString, new ChainofThoughtAgentSkill()); });
        AddFunctionMenu(Lang.Ask, () => { QuickStartChatWindow.Show(_answerString); });
    }

    private void AddFunctionMenu(string text, Action action, int xMargin = 5)
    {
        var btn = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Command = new RelayCommand(action),
            Margin = new Thickness(xMargin, 0, 0, 0),
            MinHeight = 25,
        };
        FunctionMenu.Children.Add(btn);
    }
}