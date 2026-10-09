using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 把主窗口（或标题含某段字的窗口）按当前样子渲染成 PNG：核对界面观感用，只读不点。
/// 主窗口收在托盘时先显示出来（与点托盘图标同一条路）
/// </summary>
internal sealed class UiCaptureCommand : IAsyncDevCommand
{
    public string Name => "ui.capture";

    public string Usage => "把窗口当前的样子存成 PNG（只读）。path（输出文件）、window（标题含这段字的窗口，省略取主窗口）";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        string path = Path.GetFullPath(DevCommandRegistry.RequireString(args, "path"));
        string? title = GroupDevCommands.StringOr(args, "window");
        IClassicDesktopStyleApplicationLifetime desktop =
            Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime
            ?? throw new InvalidOperationException("no desktop lifetime");
        if (title == null && UIManager.GetWindow<MainWindow>()?.IsVisible != true)
        {
            UIManager.ShowWindow<MainWindow>();
            await Task.Delay(800);
        }

        Window window = (title == null
                            ? UIManager.GetWindow<MainWindow>()
                            : desktop.Windows.LastOrDefault(x => x.Title?.Contains(title) == true))
                        ?? throw new InvalidOperationException($"no window '{title ?? "main"}'");

        double scale = window.RenderScaling;
        PixelSize size = PixelSize.FromSize(window.ClientSize, scale);
        using RenderTargetBitmap bitmap = new(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path);
        return new { path, width = size.Width, height = size.Height };
    }
}
