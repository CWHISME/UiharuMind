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
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Clipboard;

public partial class ClipboardItem : ObservableObject
{
    public ClipboardItem(string date, string text, string imageSource = "")
    {
        _date = date;
        _text = text;
        _imageSource = imageSource;
        _isImage = !string.IsNullOrEmpty(imageSource);
    }

    [ObservableProperty] private string _text;
    [ObservableProperty] private string _date;
    [ObservableProperty] private string _imageSource;
    [ObservableProperty] private bool _isImage;
    [ObservableProperty] private bool _isFavorite;

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
            // 历史项只存路径,这里现解两张:一张写剪贴板(剪贴板只借用,故当场释放),
            // 另一张交给预览窗——那个重载是接管语义,关窗时由它释放
            using (Bitmap forClipboard = new Bitmap(ImageSource))
            {
                App.Clipboard.CopyImageToClipboard(forClipboard, true);
            }

            UIManager.ShowPreviewImageWindowAtMousePosition(new Bitmap(ImageSource), horizontalAlignment: HorizontalAlignment.Center, verticalAlignment: VerticalAlignment.Center);
        }
        else
        {
            App.Clipboard.CopyToClipboard(Text, true);
        }

        // App.Services.GetRequiredService<IMessageService>().ShowNotification(Lang.CopiedToClipboardTips);
    }
}