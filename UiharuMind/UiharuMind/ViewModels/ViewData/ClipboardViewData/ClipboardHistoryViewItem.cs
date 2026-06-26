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
using Microsoft.Extensions.DependencyInjection;
using UiharuMind;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Views;
using UiharuMind.Views.Windows;

public partial class ClipboardItem : ObservableObject
{
    private readonly IMessageService _messageService;

    public ClipboardItem(string date, string text, string imageSource = "")
        : this(date, text, imageSource, App.Services.GetRequiredService<IMessageService>())
    {
    }

    public ClipboardItem(string date, string text, string imageSource, IMessageService messageService)
    {
        _date = date;
        _text = text;
        _imageSource = imageSource;
        _isImage = !string.IsNullOrEmpty(imageSource);
        _messageService = messageService;
    }

    [ObservableProperty] private string _text;
    [ObservableProperty] private string _date;
    [ObservableProperty] private string _imageSource;
    [ObservableProperty] private bool _isImage;

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

        _messageService.ShowNotification(Lang.CopiedToClipboardTips);

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
