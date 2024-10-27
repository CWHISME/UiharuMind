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
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.LLamaCpp.Versions;
using UiharuMind.Resources.Lang;

namespace UiharuMind.ViewModels.ViewData.Download;

public class RuntimeEngineDownloadListViewData : DownloadListViewData
{
    protected override string GetDeleteFilePath(DownloadableItemData version)
    {
        var ver = (VersionInfo)version.Target;
        return ver.ExecutablePath;
    }

    protected override string? GetLocalFileDirectoryPath(DownloadableItemData version)
    {
        var ver = (VersionInfo)version.Target;
        return ver.ExecutablePath;
    }

    protected override async Task OnFileDownloadCompleted(DownloadableItemData obj)
    {
        //解压完成后，刷新版本列表
        var version = (VersionInfo)obj.Target;
        obj.IsDownloading = true;
        obj.DownloadInfo = Lang.Decompressing + obj.DownloadInfo;
        await SimpleZipHelper.ExtractZipFile(obj.DownloadFilePath, version.ExecutablePath, true);
        obj.IsDownloading = false;
        obj.InitFileSize();
    }
}