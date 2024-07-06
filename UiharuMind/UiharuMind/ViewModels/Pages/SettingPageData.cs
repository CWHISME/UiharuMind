using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core;
using UiharuMind.Core.Input;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Capture;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class SettingPageData : PageDataBase
{
    public SettingPageData()
    {
        HotKeyManager.SetHotKey(View, new KeyGesture(Key.A, KeyModifiers.Alt | KeyModifiers.Shift));
    }

    [RelayCommand]
    private async Task Click()
    {
        // ScreenCaptureManager.CaptureScreen();
        await ScreenCaptureManager.GetScreenCaptureFromClipboard();
    }

    protected override Control CreateView => new SettingPage();
}