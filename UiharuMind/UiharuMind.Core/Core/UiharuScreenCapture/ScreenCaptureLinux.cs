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
using System.IO;
using System.Threading.Tasks;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.UiharuScreenCapture.Portal;

namespace UiharuMind.Core.Core.UiharuScreenCapture;

/// <summary>
/// Linux 整屏抓取：统一走 xdg-desktop-portal 的 Screenshot 接口。
///
/// 不再保留 gnome-screenshot / spectacle / scrot 等 CLI 分支：GNOME 49 起
/// gnome-screenshot 因失去私有 API 访问权而完全失效，且 CLI 的交互式选区会顶掉
/// 应用自己的选区 UI。grim 仅作为 Portal 缺席时的 wlroots 兜底保留。
/// </summary>
public class ScreenCaptureLinux : IScreenCapture
{
    private readonly ScreenshotPortalClient _portalClient = new();

    /// <summary>
    /// 探测 xdg-desktop-portal 后端是否在线，供权限引导界面区分失败原因
    /// </summary>
    /// <returns>后端在线返回 true</returns>
    public static Task<bool> IsPortalAvailableAsync()
    {
        return ScreenshotPortalClient.IsPortalAvailableAsync();
    }

    public async Task<Stream?> CaptureFullScreenAsync(string parentWindowHandle)
    {
        var (status, uri) = await _portalClient.RequestScreenshotAsync(parentWindowHandle);
        if (status != PortalStatus.Success || uri == null)
        {
            if (status == PortalStatus.Unavailable) return await CaptureWithGrimAsync();
            return null;
        }

        return await ReadAndDeleteAsync(UriToPath(uri));
    }

    /// Portal 交出的是 XDG_RUNTIME_DIR 下的临时文件，读完必须删掉，否则每次截图都留一份整屏原图
    private static async Task<Stream?> ReadAndDeleteAsync(string? path)
    {
        if (path == null || !File.Exists(path)) return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            return new MemoryStream(bytes);
        }
        catch (Exception e)
        {
            Log.Warning($"读取截图文件失败：{e.Message}");
            return null;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // 删不掉不影响本次截图
            }
        }
    }

    private static string? UriToPath(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;
    }

    /// wlroots 系（sway/Hyprland）在没装 portal 后端时仍可用 grim，作为最后兜底
    private static async Task<Stream?> CaptureWithGrimAsync()
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"UiharuCapture_{Guid.NewGuid():N}.png");
        var succeeded = await Process.ProcessHelper.StartProcess("grim", temporaryPath);
        if (!succeeded || !File.Exists(temporaryPath))
        {
            Log.Error("Portal 不可用且 grim 也未能截图，请安装 xdg-desktop-portal 对应后端。");
            return null;
        }

        return await ReadAndDeleteAsync(temporaryPath);
    }
}
