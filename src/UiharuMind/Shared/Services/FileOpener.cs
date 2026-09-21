/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Shared.Services;

/// <summary>
/// 「打开一个文件」的公共入口：文件搜索双击、markdown 链接、文本窗的打开对话框与拖放
/// 都走这里，不再各判一遍后缀、也不再各写一套图片/系统 fallback。
///
/// 顺序是刻意的：存在 → 图片（按扩展名直达贴图窗，大图走系统，
/// 读文件头再定在网络盘上是可感知的卡顿）→ 大小两档（超编辑但在查看上限内进只读窗，
/// 超查看直接系统，不读全文）→ 编码探测读文本。
/// 读不出来的一律转系统打开——`EncodingUnknown` 的二进制（如 PNG）和 `NotPlainText`
/// 在这里是同一档：界面都展示不了，系统默认程序接过去比报错有用。
/// 只有真读失败（IO 错）才弹错。
/// </summary>
public static class FileOpener
{
    private static IMessageService Messages => App.Services.GetRequiredService<IMessageService>();

    /// <summary>分流结果：文本（调用方展示）、已转交外部、失败（已报错）</summary>
    public sealed record RouteOutcome(TextFileReadResult? Text, bool Redirected);

    /// <summary>
    /// 打开一个文件（fire-and-forget 版，void 事件 handler 用这个）。
    /// 文本进编辑窗（内容搜索命中可带行号），图片进贴图窗，其余走系统。
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    /// <param name="lineNumber">文本窗打开后定位的行号，其余情况为 null</param>
    public static void Open(string filePath, int? lineNumber = null) => _ = OpenAsync(filePath, lineNumber);

    /// <summary>打开一个文件（可等待版）。分流细节见 <see cref="RouteAsync"/>。</summary>
    public static async Task OpenAsync(string filePath, int? lineNumber = null)
    {
        RouteOutcome outcome = await RouteAsync(filePath);
        if (outcome.Text == null) return; //已分流或已报错

        string text = outcome.Text.Text!;
        Encoding encoding = outcome.Text.Encoding!;
        bool hasBom = outcome.Text.HasBom;
        // 窗口可复用：每次打开都可能落在某个之前关掉的缓存实例上，装载统一放异步里做。
        // 多开不设上限——多个文件并排对着看是文件搜索场景的常见需求。
        UIManager.ShowWindow<TextFileWindow>(
            w => _ = w.LoadTextAsync(filePath, text, encoding, hasBom, lineNumber), isMulti: true);
    }

    /// <summary>
    /// 分流一个文件：读出文本返回 <c>Text</c>（调用方负责展示）；
    /// 转交外部（贴图窗/系统）置 <c>Redirected</c>；失败已报错，调用方什么都不用做。
    /// 窗已开着的流程（打开对话框 / 拖放）用这个，窗还没开的直接用 <see cref="OpenAsync"/>。
    /// </summary>
    /// <param name="filePath">文件绝对路径</param>
    public static async Task<RouteOutcome> RouteAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Messages.ShowNotification(Loc.Text(LangKey.TextFileFileMissing), severity: MessageSeverity.Error);
            return new RouteOutcome(null, false);
        }

        if (ImageFilePolicy.IsImage(filePath))
        {
            if (!TextFileOpenPolicy.IsWithinEditLimit(filePath))
            {
                await OpenWithSystemAsync(filePath);
                return new RouteOutcome(null, true);
            }

            OpenPreviewImage(filePath);
            return new RouteOutcome(null, true);
        }

        bool overEditLimit = !TextFileOpenPolicy.IsWithinEditLimit(filePath);
        if (overEditLimit && !TextFileOpenPolicy.IsWithinViewLimit(filePath))
        {
            await OpenWithSystemAsync(filePath);
            return new RouteOutcome(null, true);
        }

        TextFileReadResult result = await TextFileCodec.ReadTextAsync(filePath, default);
        if (!result.Success || result.Text == null || result.Encoding == null)
        {
            if (result.ErrorCode is "NotPlainText" or "EncodingUnknown")
            {
                await OpenWithSystemAsync(filePath);
                return new RouteOutcome(null, true);
            }

            Messages.ShowNotification(
                $"{Loc.Text(LangKey.TextFileOpenFailed)} ({result.ErrorCode})", severity: MessageSeverity.Error);
            return new RouteOutcome(null, false);
        }

        if (overEditLimit)
        {
            // 超编辑上限但在查看上限内：只读查看，无回写路径。
            // 高亮按扩展名自动判定（超 256KB 高亮服务自行退化），标题带只读后缀说明为何不可存
            string title = Path.GetFileName(filePath) + Loc.Text(LangKey.TextFileReadOnlySuffix);
            FullTextWindow.Show(title, result.Text, filePath);
            return new RouteOutcome(null, true);
        }

        return new RouteOutcome(result, false);
    }

    /// <summary>图片进自家贴图窗（可钉住对着看）；损坏打不开才退回系统</summary>
    public static void OpenPreviewImage(string filePath)
    {
        try
        {
            UIManager.ShowPreviewImageWindowAtMousePosition(new Bitmap(filePath),
                horizontalAlignment: HorizontalAlignment.Center,
                verticalAlignment: VerticalAlignment.Center);
        }
        catch (Exception e)
        {
            Log.Warning($"Preview image failed '{filePath}': {e.Message}");
            _ = OpenWithSystemAsync(filePath);
        }
    }

    /// <summary>
    /// 交系统默认程序打开：macOS 走 open 并按退出码回落到揭示所在目录，其余 Process.Start。
    /// 失败才提示（调用方已决定不自己接）。
    /// </summary>
    public static async Task OpenWithSystemAsync(string filePath)
    {
        try
        {
            if (PlatformUtils.IsMacOS)
            {
                var result = await Cli.Wrap("open")
                    .WithArguments($"\"{filePath}\"")
                    // 设置为 None，这样当 ExitCode != 0 时，CliWrap 不会抛出异常，
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync();
                if (result.ExitCode != 0) RevealInFolder(filePath);
                return;
            }

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Messages.ShowNotification(
                $"{Loc.Text(LangKey.TextFileOpenFailed)} ({e.Message})", severity: MessageSeverity.Error);
        }
    }

    /// <summary>在系统文件管理器里露出这个文件（或它所在的目录）</summary>
    /// <param name="fullPath">文件或目录的绝对路径</param>
    public static void RevealInFolder(string fullPath)
    {
        string? dir = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        if (Directory.Exists(dir))
        {
            if (PlatformUtils.IsWindows)
            {
                Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
            else if (PlatformUtils.IsMacOS)
            {
                Process.Start("open", $"-R \"{fullPath}\"");
            }
            else App.FilesService.OpenFolder(dir);
        }
        else
            App.Services.GetRequiredService<IMessageService>().ShowNotification("Directory not found");
    }
}
