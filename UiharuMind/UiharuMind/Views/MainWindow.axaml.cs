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
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views.Common;
using Ursa.Controls;

namespace UiharuMind.Views;

public partial class MainWindow : UiharuWindowBase
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // public override void Show()
    // {
    //     base.Show();
    //     // ShowInTaskbar = true;
    // }
    //
    // protected override void OnClosing(WindowClosingEventArgs e)
    // {
    //     // UiharuCoreManager.Instance.Input.Stop();
    //     base.OnClosing(e);
    //     Hide();
    //     e.Cancel = true;
    // }
    //
    // protected override void OnClosed(EventArgs e)
    // {
    //     base.OnClosed(e);
    //     // ShowInTaskbar = false;
    // }
}