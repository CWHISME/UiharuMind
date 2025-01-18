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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind;
using UiharuMind.Resources.Lang;
using UiharuMind.Views;
using UiharuMind.Views.Windows;

public partial class ClipboardItem(string date, string text, string imageSource = "") : ObservableObject
{
    [ObservableProperty] private string _text = text;
    [ObservableProperty] private string _date = date;
    [ObservableProperty] private string _imageSource = imageSource;
    [ObservableProperty] private bool _isImage = !string.IsNullOrEmpty(imageSource);

    // public ClipboardItem()
    // {
    // }

    public ClipboardItem(string text) : this(System.DateTime.Now.ToString("(yyyy-MM-dd HH:mm:ss)"), text)
    {
    }

    public void CopyToClipboard()
    {
        if (IsImage)
        {
            // App.Clipboard.CopyImageToClipboard(ImageSource);
        }
        else
        {
            App.Clipboard.CopyToClipboard(Text);
            App.MessageService.ShowToast(Lang.CopiedToClipboardTips);
            UIManager.CloseWindow<QuickClipboardHistoryWindow>();
        }
    }
}