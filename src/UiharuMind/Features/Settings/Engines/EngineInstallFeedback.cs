using System;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// 「确保有引擎」各处的提示说法
/// </summary>
/// <param name="NoPackage">找不到本机能用的推荐包</param>
/// <param name="Queued">排进了队列（{0} 为版本名）</param>
/// <param name="CheckFailed">查不了（{0} 为原因）</param>
public sealed record EngineEnsureTexts(LangKey NoPackage, LangKey Queued, LangKey CheckFailed);

/// <summary>
/// 引擎包排进下载队列前后的提示：排上了说一句，装好、失败各再说一句。
/// 设置页手动更新、下模型时顺带装引擎、语音分类一键装引擎共用这一份
/// </summary>
public static class EngineInstallFeedback
{
    /// <summary>
    /// 本机没有引擎就把推荐包排进队列，并按结果提示（在后台线程查，会联网）
    /// </summary>
    /// <param name="ensure">查并排包</param>
    /// <param name="messages">提示</param>
    /// <param name="texts">各结果的说法</param>
    /// <param name="onFailed">下载或安装失败时（界面线程）</param>
    /// <returns>结果；查不了为 null</returns>
    public static async Task<EEngineEnsureState?> EnsureAsync(
        Func<Task<(EEngineEnsureState State, EnginePackage? Version, DownloadJob? Job)>> ensure,
        IMessageService messages, EngineEnsureTexts texts, Action? onFailed = null)
    {
        try
        {
            (EEngineEnsureState state, EnginePackage? version, DownloadJob? job) = await Task.Run(ensure);
            if (state == EEngineEnsureState.NoPackage)
                messages.ShowNotification(Loc.Text(texts.NoPackage), severity: MessageSeverity.Warning);
            else if (state == EEngineEnsureState.Queued && version != null && job != null)
            {
                messages.ShowNotification(Loc.Text(texts.Queued, version.Name));
                Watch(job, version.Name, messages, onFailed);
            }

            return state;
        }
        catch (Exception e)
        {
            Log.Warning($"Ensure engine failed: {e.Message}");
            messages.ShowNotification(Loc.Text(texts.CheckFailed, e.Message), severity: MessageSeverity.Warning);
            return null;
        }
    }

    /// <summary>
    /// 盯一个引擎包下载项，装好、失败各提示一次（状态在下载线程上变，提示切回界面线程）
    /// </summary>
    /// <param name="job">下载项</param>
    /// <param name="versionName">版本名</param>
    /// <param name="messages">提示</param>
    /// <param name="onFailed">失败时（界面线程）</param>
    public static void Watch(DownloadJob job, string versionName, IMessageService messages, Action? onFailed = null)
    {
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DownloadJob.State)) return;
            if (job.State == EDownloadJobState.Completed)
                UiDispatcher.FireAndForget(() => messages.ShowNotification(
                    Loc.Text(LangKey.ModelDownloadEngineInstalled, versionName), severity: MessageSeverity.Success));
            else if (job.State == EDownloadJobState.Failed)
                UiDispatcher.FireAndForget(() =>
                {
                    onFailed?.Invoke();
                    messages.ShowNotification(
                        Loc.Text(LangKey.ModelDownloadEngineFailed, versionName, job.Error?.Message ?? ""),
                        severity: MessageSeverity.Error);
                });
        };
    }
}
