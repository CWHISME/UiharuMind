using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind;
using UiharuMind.Resources.Lang;
using UiharuMind.Views;
using UiharuMind.Views.Windows;

public partial class ClipboardItem : ObservableObject
{
    [ObservableProperty] private string _text;
    [ObservableProperty] private string _date;
    [ObservableProperty] private string _imageSource;
    [ObservableProperty] private bool _isImage;

    public ClipboardItem()
    {
    }

    public ClipboardItem(string text) : this(System.DateTime.Now.ToString("(yyyy-MM-dd HH:mm:ss)"), text)
    {
    }

    public ClipboardItem(string date, string text, string imageSource = "")
    {
        _text = text;
        _date = date;
        _imageSource = imageSource;
        _isImage = !string.IsNullOrEmpty(imageSource);
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