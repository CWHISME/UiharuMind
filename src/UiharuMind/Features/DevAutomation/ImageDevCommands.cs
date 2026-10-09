using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using UiharuMind.Features.Models;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Features.Models.Downloads.Image;
using UiharuMind.Features.Models.ImageModels;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 本地生图冒烟用的几步：在「获取模型」的生图分类里点下载、列生图模型、从生图模型页签打开画图窗口画一张。
/// 下载等完用 model.wait（同一个下载区）
/// </summary>
internal static class ImageDevCommands
{
    /// <summary>造出全部生图步骤</summary>
    /// <returns>步骤集合</returns>
    public static IReadOnlyList<IDevCommand> CreateAll() =>
    [
        new ImageDownloadCommand(),
        new ImageListCommand(),
        new ImageDrawCommand(),
    ];

    internal static object Describe(ImageModelItemViewData item) => new
    {
        name = item.Name,
        local = item.IsLocal,
        detail = item.Detail,
        editing = item.SupportsEditing,
        ready = item.IsReady,
        status = item.StatusText
    };

    internal static async Task<ImageDrawWindow> WaitForDrawWindow()
    {
        for (int i = 0; i < 50; i++)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.Windows.OfType<ImageDrawWindow>().LastOrDefault() is { } window)
                return window;
            await Task.Delay(100);
        }

        throw new TimeoutException("draw window never opened");
    }
}

/// <summary>
/// 在「获取模型」的生图分类里选家族、点某一档的下载（引擎没装会一起排上）。<c>args</c>：family（家族 id）、tier（省略取推荐）
/// </summary>
internal sealed class ImageDownloadCommand : IAsyncDevCommand
{
    public string Name => "image.download";

    public string Usage => "跳到「获取模型」的生图分类，选家族点某一档的下载，不等下完（会联网，引擎没装一起排上）。family（家族 id，如 z-image-turbo）、tier（lite / balanced / quality，省略取推荐）。文件已在本机时改点「画一张」把它加进生图模型（随即关窗）";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        string family = DevCommandRegistry.RequireString(args, "family");
        string? tier = GroupDevCommands.StringOr(args, "tier");
        ModelPageData page = ModelDevCommands.JumpToModelPage();
        page.ShowImageDownloads();
        ImageMarketData market = page.Downloads!.Market<ImageMarketData>();
        market.SearchText = "";
        market.SelectedFamily = market.Families.FirstOrDefault(x => x.Family.Id == family)
                                ?? throw new InvalidOperationException(
                                    $"no image family '{family}': {string.Join(", ", market.Families.Select(x => x.Family.Id))}");

        ImageFamilyDetailData detail = market.Detail!;
        Stopwatch watch = Stopwatch.StartNew();
        while (detail.IsLoading)
        {
            if (watch.Elapsed > TimeSpan.FromMinutes(1)) throw new TimeoutException($"image family '{family}' never loaded");
            await Task.Delay(200);
        }

        ImageTierRowData row = (tier == null
                                   ? detail.Rows.FirstOrDefault(x => x.IsRecommended)
                                   : detail.Rows.FirstOrDefault(x => x.Tier.Id == tier))
                               ?? throw new InvalidOperationException(
                                   $"no tier '{tier ?? "recommended"}' in {family}: {string.Join(", ", detail.Rows.Select(x => x.Tier.Id))}");
        // 文件已在本机（多是别的档案下过）时界面上是「画一张」而不是下载：引擎从安装引导装，条目经「画一张」加进列表
        if (row.Status.State == EQuantDownloadState.Downloaded)
        {
            if (!await market.EnginePrompt.RefreshAsync()) await market.EnginePrompt.InstallCommand.ExecuteAsync(null);
            detail.DrawCommand.Execute(row);
            (await ImageDevCommands.WaitForDrawWindow()).Close();
        }
        else if (row.CanDownload)
        {
            await detail.DownloadCommand.ExecuteAsync(row);
        }
        else
        {
            throw new InvalidOperationException($"tier '{row.Tier.Id}' is {row.Status.State}, neither downloadable nor installed");
        }

        return new { family, tier = row.Tier.Id, size = row.SizeText, state = row.Status.State.ToString() };
    }
}

/// <summary>列出生图模型页签里的条目（按回退顺序）</summary>
internal sealed class ImageListCommand : IDevCommand
{
    public string Name => "image.list";

    public string Usage => "列出生图模型（按回退顺序：名字、是否本地、能否改图、状态）";

    public object? Execute(JsonElement args)
    {
        ModelPageData page = ModelDevCommands.JumpToModelPage();
        page.ShowImageModels();
        return page.ImageModels.Items.Select(ImageDevCommands.Describe).ToList();
    }
}

/// <summary>
/// 在生图模型页签点某一行的「试画」，在画图窗口里填提示词画一张，画完关窗。
/// <c>args</c>：model（生图模型名）、prompt、images（参考图路径，带了就是改图）、timeoutMinutes（默认 20，到了点停止）、copyTo（把图另拷一份到这个路径）、
/// memoryRisk（本地引擎说内存不够时：refuse 默认，照实失败；allow 点窗口里的「仍然加载」）
/// </summary>
internal sealed class ImageDrawCommand : IAsyncDevCommand
{
    public string Name => "image.draw";

    public string Usage => "在生图模型页签点某一行的「试画」，填提示词画一张后关窗（本地模型会起引擎）。model、prompt、images（参考图路径数组，带了就是改图）、timeoutMinutes（默认 20，到了点停止）、copyTo（另拷一份到这个文件路径，如工作区里，好让会话接着改它）、memoryRisk（本地引擎说此刻内存不够时：refuse 默认照实失败，allow 点窗口里的「仍然加载」）";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        string model = DevCommandRegistry.RequireString(args, "model");
        string prompt = DevCommandRegistry.RequireString(args, "prompt");
        TimeSpan timeout = TimeSpan.FromMinutes(GroupDevCommands.IntOr(args, "timeoutMinutes", 20));
        ModelPageData page = ModelDevCommands.JumpToModelPage();
        page.ShowImageModels();
        ImageModelItemViewData item = page.ImageModels.Items.FirstOrDefault(x => x.Name == model)
                                      ?? throw new InvalidOperationException(
                                          $"no image model '{model}': {string.Join(", ", page.ImageModels.Items.Select(x => x.Name))}");

        page.ImageModels.DrawCommand.Execute(item);
        ImageDrawWindow window = await ImageDevCommands.WaitForDrawWindow();
        ImageDrawViewData draw = (ImageDrawViewData)window.DataContext!;
        draw.Prompt = prompt;
        bool allowMemoryRisk = GroupDevCommands.StringOr(args, "memoryRisk") == "allow";
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("images", out JsonElement images))
        {
            await draw.References.AddAsync(images.EnumerateArray().Select(x => Path.GetFullPath(x.GetString()!)));
            if (draw.References.Items.Count != images.GetArrayLength())
            {
                window.Close();
                throw new InvalidOperationException("some reference images could not be added");
            }
        }

        Stopwatch watch = Stopwatch.StartNew();
        await RunAsync(draw, draw.GenerateCommand.ExecuteAsync(null), timeout - watch.Elapsed);
        string? memoryRisk = draw.MemoryRiskText;
        if (memoryRisk != null && allowMemoryRisk)
            await RunAsync(draw, draw.GenerateAnywayCommand.ExecuteAsync(null), timeout - watch.Elapsed);
        if (draw.ResultPath == null)
        {
            window.Close();
            throw new InvalidOperationException($"no image: {draw.MemoryRiskText ?? draw.StatusText}");
        }

        string? copy = null;
        if (GroupDevCommands.StringOr(args, "copyTo") is { } target)
        {
            copy = Path.GetFullPath(target);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(draw.ResultPath, copy, true);
        }

        object result = new
        {
            model = draw.SelectedModel?.Name,
            status = draw.StatusText,
            memoryRisk,
            path = draw.ResultPath,
            copy,
            seconds = (int)watch.Elapsed.TotalSeconds
        };
        window.Close();
        return result;
    }

    // 到点没画完就点停止
    private static async Task RunAsync(ImageDrawViewData draw, Task run, TimeSpan timeout)
    {
        if (await Task.WhenAny(run, Task.Delay(timeout > TimeSpan.Zero ? timeout : TimeSpan.Zero)) != run)
            draw.StopCommand.Execute(null);
        await run;
    }
}
