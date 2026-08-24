using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.UiharuScreenCapture.Portal;

/// <summary>
/// Portal 调用的三态结果。区分「没有后端」与「调用失败」，是因为前者应当引导用户安装
/// xdg-desktop-portal，后者通常是用户拒绝授权，两种提示完全不同。
/// </summary>
public enum PortalStatus
{
    Success,
    Unavailable,
    Failed
}

/// <summary>
/// org.freedesktop.portal.Screenshot 的最小客户端。
///
/// Wayland 不允许客户端自行抓屏，Portal 是唯一的官方通道；GNOME 49 起更把
/// org.gnome.Shell.Screenshot 收为私有 API（并因此让 gnome-screenshot 本身失效），
/// 所以这里不保留任何 CLI 回退。
/// </summary>
internal sealed class ScreenshotPortalClient
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalObjectPath = "/org/freedesktop/portal/desktop";
    private const string ScreenshotInterface = "org.freedesktop.portal.Screenshot";
    private const string RequestInterface = "org.freedesktop.portal.Request";

    /// Portal 是异步协议，超时后视为失败。取 15 秒与 Flameshot 一致：
    /// 足够容纳 GNOME 首次弹出的授权框，又不至于让快捷键看起来卡死
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 探测 xdg-desktop-portal 后端是否在线。
    /// 权限引导界面据此把「没装 portal」与「用户拒绝授权」两种失败分开陈述。
    /// </summary>
    /// <returns>后端在线返回 true</returns>
    public static async Task<bool> IsPortalAvailableAsync()
    {
        try
        {
            var connection = DBusConnection.Session;
            await connection.ConnectAsync().ConfigureAwait(false);

            return await CallNameHasOwnerAsync(connection).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Debug($"探测 Portal 可用性失败：{e.Message}");
            return false;
        }
    }

    /// MessageWriter 是 ref struct，不能跨 await 存活，因此消息构造必须留在同步方法里
    private static Task<bool> CallNameHasOwnerAsync(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            "org.freedesktop.DBus",
            "/org/freedesktop/DBus",
            "org.freedesktop.DBus",
            "NameHasOwner",
            "s",
            MessageFlags.None);
        writer.WriteString(PortalService);

        return connection.CallMethodAsync(writer.CreateMessage(),
            static (Message message, object? state) => message.GetBodyReader().ReadBool(),
            null);
    }

    /// <summary>
    /// 请求一张整屏截图
    /// </summary>
    /// <param name="parentWindowHandle">
    /// Portal 的 parent_window 句柄。X11/XWayland 下为 "x11:0x{窗口ID}"，纯 Wayland 下为 "wayland:"。
    /// 不可传空串：xdg-desktop-portal-gnome 46 起会拒绝空句柄。
    /// </param>
    /// <returns>调用状态与截图文件 URI</returns>
    public async Task<(PortalStatus Status, string? Uri)> RequestScreenshotAsync(string parentWindowHandle)
    {
        try
        {
            var connection = DBusConnection.Session;
            await connection.ConnectAsync().ConfigureAwait(false);

            string token = $"uiharu_{Guid.NewGuid():N}";
            string requestPath = BuildRequestPath(connection.UniqueName, token);

            var completion = new TaskCompletionSource<(uint Response, string? Uri)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using var subscription = await connection.WatchSignalAsync(
                PortalService,
                RequestInterface,
                requestPath,
                "Response",
                ReadResponse,
                (exception, value) =>
                {
                    if (exception != null) completion.TrySetException(exception);
                    else completion.TrySetResult(value);
                },
                null,
                false,
                ObserverFlags.None).ConfigureAwait(false);

            await CallScreenshotAsync(connection, parentWindowHandle, token).ConfigureAwait(false);

            var finished = await Task.WhenAny(completion.Task, Task.Delay(ResponseTimeout)).ConfigureAwait(false);
            if (finished != completion.Task)
            {
                Log.Warning("Portal 截图请求超时。");
                return (PortalStatus.Failed, null);
            }

            var (response, uri) = await completion.Task.ConfigureAwait(false);
            if (response != 0)
            {
                // 1 = 用户取消，2 = 其他失败；两者都不该再往下走
                Log.Debug($"Portal 截图未完成，response={response}。");
                return (PortalStatus.Failed, null);
            }

            return string.IsNullOrEmpty(uri) ? (PortalStatus.Failed, null) : (PortalStatus.Success, uri);
        }
        catch (DBusErrorReplyException e) when (IsServiceUnknown(e))
        {
            Log.Warning("未发现 xdg-desktop-portal 后端，Wayland 下无法截图。");
            return (PortalStatus.Unavailable, null);
        }
        catch (Exception e)
        {
            Log.Warning($"Portal 截图失败：{e.Message}");
            return (PortalStatus.Failed, null);
        }
    }

    private static bool IsServiceUnknown(DBusErrorReplyException exception)
    {
        return exception.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown"
            or "org.freedesktop.DBus.Error.NameHasNoOwner";
    }

    private static Task CallScreenshotAsync(DBusConnection connection, string parentWindowHandle, string token)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            PortalService,
            PortalObjectPath,
            ScreenshotInterface,
            "Screenshot",
            "sa{sv}",
            MessageFlags.None);
        writer.WriteString(parentWindowHandle);
        writer.WriteDictionary(new Dictionary<string, VariantValue>
        {
            ["handle_token"] = VariantValue.String(token),
            // 非交互：只要整屏原图，选区交给应用自绘的遮罩窗，以保住放大镜/贴图/OCR 全套交互
            ["interactive"] = VariantValue.Bool(false)
        });

        return connection.CallMethodAsync(writer.CreateMessage());
    }

    private static (uint Response, string? Uri) ReadResponse(Message message, object? state)
    {
        var reader = message.GetBodyReader();
        uint response = reader.ReadUInt32();
        var results = reader.ReadDictionaryOfStringToVariantValue();
        string? uri = results.TryGetValue("uri", out var value) ? value.GetString() : null;
        return (response, uri);
    }

    /// Request 对象路径由 Portal 规范固定推导得出，必须在发起调用之前就订阅，
    /// 否则可能错过立即返回的 Response 信号
    private static string BuildRequestPath(string uniqueName, string token)
    {
        string sender = uniqueName.TrimStart(':').Replace('.', '_');
        return $"{PortalObjectPath}/request/{sender}/{token}";
    }
}
