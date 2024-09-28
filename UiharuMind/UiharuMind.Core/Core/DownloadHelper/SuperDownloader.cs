using System.ComponentModel;
using System.Runtime.CompilerServices;
using Downloader;

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// 封装 Downloader 的类
/// </summary>
public static class SuperDownloader
{

    // public static async void Download(string url, string savePath)
    // {
    //     IDownload downloadable = DownloadBuilder.New()
    //         .WithUrl(@"https://host.com/test-file.zip")
    //         .WithDirectory(@"C:\temp")
    //         .WithFileName("test-file.zip")
    //         .WithConfiguration(new DownloadConfiguration())
    //         .Build();
    //
    //     downloadable.DownloadProgressChanged += DownloadProgressChanged;
    //     downloadable.DownloadFileCompleted += DownloadFileCompleted;
    //     downloadable.DownloadStarted += DownloadStarted;
    //     downloadable.ChunkDownloadProgressChanged += ChunkDownloadProgressChanged;
    //
    //     await downloadable.StartAsync();
    //
    //     downloadable.Stop();
    // }
    //
    // public static async void DownloadToStream(string url, string saveDirectory, )
    // {
    //     var downloadOpt = new DownloadConfiguration()
    //     {
    //     };
    //     var downloader = new DownloadService(downloadOpt);
    //     downloader.DownloadStarted += DownloadStarted;
    //     downloader.DownloadProgressChanged += DownloadProgressChanged;
    //     downloader.ChunkDownloadProgressChanged += ChunkDownloadProgressChanged;
    //     downloader.DownloadFileCompleted += DownloadFileCompleted;
    //
    //     await downloader.DownloadFileTaskAsync(url, new DirectoryInfo(saveDirectory));
    // }
}