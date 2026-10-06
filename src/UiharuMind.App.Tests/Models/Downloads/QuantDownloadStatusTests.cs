using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Models.Downloads;

namespace UiharuMind.App.Tests.Models.Downloads;

/// <summary>
/// 行状态：多个文件合成一个量化的状态，下载项优先于磁盘残留，同名冲突只在没下完时生效
/// </summary>
public class QuantDownloadStatusTests
{
    private static QuantFileSnapshot File(bool exists = false, EDownloadJobState? job = null, long received = 0) =>
        new(100, exists, job, received);

    [Fact]
    public void AllFilesOnDisk_IsDownloaded_EvenWithSameNameElsewhere()
    {
        QuantDownloadStatus status = QuantDownloadStatus.Resolve([File(true), File(true)], hasNameConflict: true);
        Assert.Equal(EQuantDownloadState.Downloaded, status.State);
        Assert.False(status.CanDownload);
    }

    [Fact]
    public void NameConflict_BlocksDownload()
    {
        QuantDownloadStatus status = QuantDownloadStatus.Resolve([File()], hasNameConflict: true);
        Assert.Equal(EQuantDownloadState.NameConflict, status.State);
        Assert.False(status.CanDownload);
    }

    [Fact]
    public void RunningShard_IsDownloading_WithProgressAcrossShards()
    {
        QuantDownloadStatus status = QuantDownloadStatus.Resolve(
            [File(true), File(job: EDownloadJobState.Running, received: 50)], false);
        Assert.Equal(new QuantDownloadStatus(EQuantDownloadState.Downloading, 75), status);
    }

    [Theory]
    [InlineData(EDownloadJobState.Queued, EQuantDownloadState.Queued, false)]
    [InlineData(EDownloadJobState.Paused, EQuantDownloadState.Paused, true)]
    [InlineData(EDownloadJobState.Failed, EQuantDownloadState.Failed, true)]
    public void JobState_MapsToRowState(EDownloadJobState job, EQuantDownloadState expected, bool canDownload)
    {
        QuantDownloadStatus status = QuantDownloadStatus.Resolve([File(job: job)], false);
        Assert.Equal(expected, status.State);
        Assert.Equal(canDownload, status.CanDownload);
    }

    [Fact]
    public void LeftoverPartAfterRestart_IsPartial_AndClickable()
    {
        QuantDownloadStatus status = QuantDownloadStatus.Resolve([File(received: 30)], false);
        Assert.Equal(new QuantDownloadStatus(EQuantDownloadState.Partial, 30), status);
        Assert.True(status.CanDownload);
    }

    [Fact]
    public void NothingYet_IsNotDownloaded()
    {
        Assert.Equal(EQuantDownloadState.NotDownloaded, QuantDownloadStatus.Resolve([File()], false).State);
    }
}
