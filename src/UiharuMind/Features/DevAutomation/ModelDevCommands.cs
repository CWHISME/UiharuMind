using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Models;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.DevAutomation;

/// <summary>
/// 获取模型冒烟用的几步：在「获取模型」里打开仓库点下载、等下载区跑完、看本地模型列表。
/// 同样只走用户那条路：贴仓库名进搜索框，在量化行上点下载
/// </summary>
internal static class ModelDevCommands
{
    /// <summary>造出全部模型步骤</summary>
    /// <returns>步骤集合</returns>
    public static IReadOnlyList<IDevCommand> CreateAll() =>
    [
        new ModelDownloadCommand(),
        new ModelWaitCommand(),
        new ModelListCommand(),
    ];

    // 与点左栏「模型」同一条路
    internal static ModelPageData JumpToModelPage()
    {
        App.ViewModel.JumpToPage(MenuPages.MenuModelKey);
        return (ModelPageData)App.ViewModel.Content!;
    }

    internal static object Describe(ModelRunningData model) => new
    {
        name = model.ModelName,
        path = model.ModelPath,
        remote = model.IsRemoteModel,
        running = model.IsRunning
    };
}

/// <summary>
/// 贴 owner/repo 进搜索框打开仓库，点某个量化行的下载（与用户点的是同一个命令，引擎没装会一起排上）。
/// <c>args</c>：repo、quant（量化名，省略取推荐）、projector（是否带视觉投影，默认 true）
/// </summary>
internal sealed class ModelDownloadCommand : IAsyncDevCommand
{
    public string Name => "model.download";

    public string Usage => "跳到模型页，在「获取模型」里打开仓库并点下载，不等下完（会联网）。repo（owner/repo）、quant（量化名，省略取推荐）、projector（是否带视觉投影，默认 true）";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        string repo = DevCommandRegistry.RequireString(args, "repo");
        ModelPageData page = ModelDevCommands.JumpToModelPage();
        page.ShowDownloads(repo);

        Stopwatch watch = Stopwatch.StartNew();
        ModelRepoDetailData? detail;
        while ((detail = page.Downloads?.Detail) == null || detail.Repository != repo || detail.IsLoading)
        {
            if (watch.Elapsed > TimeSpan.FromMinutes(1)) throw new TimeoutException($"repository '{repo}' never loaded");
            await Task.Delay(200);
        }

        if (detail.ErrorText != null || detail.NeedsToken)
            throw new InvalidOperationException(detail.ErrorText ?? $"repository '{repo}' needs a token");

        string? quant = GroupDevCommands.StringOr(args, "quant");
        ModelQuantRowData row = (quant == null
                                    ? detail.Rows.FirstOrDefault(x => x.IsRecommended)
                                    : detail.Rows.FirstOrDefault(x =>
                                        string.Equals(x.Label, quant, StringComparison.OrdinalIgnoreCase)))
                                ?? throw new InvalidOperationException(
                                    $"no quant '{quant ?? "recommended"}' in {repo}: {string.Join(", ", detail.Rows.Select(x => x.Label))}");
        detail.IncludeProjector = GroupDevCommands.BoolOr(args, "projector", true);
        await detail.DownloadCommand.ExecuteAsync(row);
        return new { repo, quant = row.Label, file = row.MainFilePath, size = row.SizeText, state = row.Status.State.ToString() };
    }
}

/// <summary>
/// 等下载区里排着、在下的都结束（完成、失败或暂停），再等模型列表刷新出来。<c>args</c>：timeoutMinutes
/// </summary>
internal sealed class ModelWaitCommand : IAsyncDevCommand
{
    public string Name => "model.wait";

    public string Usage => "等下载区排着与在下的都结束（含下完顺手加载的模型），结果带每项状态、当前模型与本地模型列表。timeoutMinutes（默认 30）";

    public async Task<object?> ExecuteAsync(JsonElement args)
    {
        TimeSpan timeout = TimeSpan.FromMinutes(GroupDevCommands.IntOr(args, "timeoutMinutes", 30));
        Stopwatch watch = Stopwatch.StartNew();
        bool timedOut = false;
        while (DownloadQueue.Shared.Jobs.Any(x => x.State is EDownloadJobState.Queued or EDownloadJobState.Running))
        {
            if (watch.Elapsed > timeout)
            {
                timedOut = true;
                break;
            }

            await Task.Delay(1000);
        }

        // 完成回调里还要刷模型列表、装引擎、可能顺手加载，给它们一点时间，再等加载结束
        await Task.Delay(2000);
        while (App.ModelService.IsLoading && watch.Elapsed < timeout) await Task.Delay(500);
        return new
        {
            timedOut,
            current = App.ModelService.CurModelRunningData is { } current ? ModelDevCommands.Describe(current) : null,
            elapsedSeconds = (int)watch.Elapsed.TotalSeconds,
            jobs = DownloadQueue.Shared.Jobs.Select(x => new
            {
                name = x.Name,
                state = x.State.ToString(),
                error = x.Error?.Message
            }).ToList(),
            models = App.ModelService.ModelSources.Where(x => !x.IsRemoteModel).Select(ModelDevCommands.Describe).ToList()
        };
    }
}

/// <summary>列出模型页里的对话模型</summary>
internal sealed class ModelListCommand : IDevCommand
{
    public string Name => "model.list";

    public string Usage => "列出对话模型（名字、路径、是否远程、是否在跑）。local（true 只看本地）";

    public object? Execute(JsonElement args)
    {
        bool localOnly = GroupDevCommands.BoolOr(args, "local", false);
        return App.ModelService.ModelSources
            .Where(x => !localOnly || !x.IsRemoteModel)
            .Select(ModelDevCommands.Describe)
            .ToList();
    }
}
