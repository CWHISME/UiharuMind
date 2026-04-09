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

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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

    public ClipboardItem() : this("", "")
    {
    }

    public ClipboardItem(string text) : this(System.DateTime.Now.ToString("(yyyy-MM-dd HH:mm:ss)"), text)
    {
    }

    public void CopyToClipboard()
    {
        App.Clipboard.MoveClipboardHistoryItemFirst(this);
        if (IsImage)
        {
            var image = new Bitmap(ImageSource);
            App.Clipboard.CopyImageToClipboard(image, true);
            UIManager.ShowPreviewImageWindowAtMousePosition(image, horizontalAlignment: HorizontalAlignment.Center, verticalAlignment: VerticalAlignment.Center);
        }
        else
        {
            App.Clipboard.CopyToClipboard(Text, true);
        }

        App.MessageService.ShowToast(Lang.CopiedToClipboardTips);

        // Task.Run(() =>
        // {
        //     // Task.Delay(100).ContinueWith((x) =>
        //     // {
        //     Dispatcher.UIThread.Post(UIManager.CloseWindow<QuickClipboardHistoryWindow>,
        //         DispatcherPriority.ApplicationIdle);
        //     // });
        // });
        // Dispatcher.UIThread.Post(UIManager.CloseWindow<QuickClipboardHistoryWindow>,
        //     DispatcherPriority.ApplicationIdle);
    }
}