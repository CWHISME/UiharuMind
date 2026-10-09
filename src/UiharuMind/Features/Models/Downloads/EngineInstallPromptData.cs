using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 安装引导的文案
/// </summary>
/// <param name="Hint">引导语</param>
/// <param name="Action">按钮字</param>
/// <param name="Installing">排上之后、装好之前的说明</param>
/// <param name="Ensure">排包结果的提示</param>
public sealed record EngineInstallPromptTexts(string Hint, string Action, string Installing, EngineEnsureTexts Ensure);

/// <summary>
/// 「获取模型」里某个分类要的引擎没装时的安装引导：本机有没有、一键把推荐包排进全局队列、排上后转圈到装好。
/// 与下载对话模型时自动装 llama.cpp 同一条路
/// </summary>
public partial class EngineInstallPromptData : ObservableObject
{
    private readonly IEngineDistribution _engine;
    private readonly IMessageService _messages;
    private bool _isWatching;

    [ObservableProperty] private bool _hasEngine = true;
    [ObservableProperty] private bool _isQueued;

    /// <param name="engine">引擎分发</param>
    /// <param name="messages">提示</param>
    /// <param name="texts">文案</param>
    public EngineInstallPromptData(IEngineDistribution engine, IMessageService messages, EngineInstallPromptTexts texts)
    {
        _engine = engine;
        _messages = messages;
        Texts = texts;
    }

    /// <summary>
    /// 文案
    /// </summary>
    public EngineInstallPromptTexts Texts { get; }

    /// <summary>
    /// 引擎装好、换了或删了版本（界面线程）
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 开始跟着引擎版本变（分类第一次打开时调，没打开过的分类不挂）
    /// </summary>
    public void Watch()
    {
        if (_isWatching) return;
        _isWatching = true;
        _engine.VersionChanged += () => UiDispatcher.FireAndForget(async () =>
        {
            await RefreshAsync();
            Changed?.Invoke();
        });
    }

    /// <summary>
    /// 重看本机有没有引擎（扫盘在后台）
    /// </summary>
    /// <returns>有引擎</returns>
    public async Task<bool> RefreshAsync()
    {
        bool hasEngine = await Task.Run(async () => _engine.CurrentVersion != null ||
                                                    (await _engine.GetLocalVersionsAsync().ConfigureAwait(false))
                                                    .Any(x => x.IsInstalled));
        HasEngine = hasEngine;
        if (hasEngine) IsQueued = false;
        return hasEngine;
    }

    [RelayCommand]
    private async Task Install()
    {
        IsQueued = true;
        EEngineEnsureState? state = await EngineInstallFeedback.EnsureAsync(() => _engine.EnsureQueuedAsync(),
            _messages, Texts.Ensure, () => IsQueued = false);
        if (state == EEngineEnsureState.AlreadyInstalled)
        {
            await RefreshAsync();
            Changed?.Invoke();
        }
        else if (state != EEngineEnsureState.Queued)
        {
            IsQueued = false;
        }
    }
}
