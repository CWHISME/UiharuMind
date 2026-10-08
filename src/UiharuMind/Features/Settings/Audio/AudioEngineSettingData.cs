using System;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings.Audio;

/// <summary>
/// 本地模型（语音）页：audio.cpp 引擎卡、合成 / 听写用哪个模型与音色、获取模型、运行参数
/// </summary>
public sealed class AudioEngineSettingData : IDisposable
{
    private readonly AudioCppProvider _provider;

    public AudioEngineSettingData() : this(App.Services.GetRequiredService<IMessageService>(), AudioCppEngine.Shared,
        AudioCppPackageStore.Shared, DownloadQueue.Shared, path => App.FilesService.OpenFolder(path))
    {
    }

    /// <param name="messageService">提示与确认</param>
    /// <param name="engine">语音引擎</param>
    /// <param name="store">模型包的下载与删除</param>
    /// <param name="queue">下载队列</param>
    /// <param name="openFolder">打开目录</param>
    public AudioEngineSettingData(IMessageService messageService, AudioCppEngine engine, AudioCppPackageStore store,
        DownloadQueue queue, Action<string> openFolder)
    {
        _provider = new AudioCppProvider(engine);
        Engine = new EngineVersionsViewData(new AudioCppEngineVersionSource(engine), messageService, openFolder);
        Models = new AudioModelSelectionViewData(engine, _provider.GetVoicesAsync, messageService);
        Catalog = new AudioModelCatalogViewData(engine, store, queue, messageService, Models.Refresh);
        Runtime = new AudioCppRuntimeSettingsViewData(engine);
        // 引擎装好或换了版本，可下的模型包跟着变（spec 随引擎走）
        Engine.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EngineVersionsViewData.SelectedVersion))
                Dispatcher.UIThread.Post(Catalog.Refresh);
        };
        Catalog.Refresh();
    }

    /// <summary>
    /// audio.cpp 引擎卡
    /// </summary>
    public EngineVersionsViewData Engine { get; }

    /// <summary>
    /// 合成 / 听写用的模型与音色
    /// </summary>
    public AudioModelSelectionViewData Models { get; }

    /// <summary>
    /// 获取语音模型
    /// </summary>
    public AudioModelCatalogViewData Catalog { get; }

    /// <summary>
    /// 运行参数
    /// </summary>
    public AudioCppRuntimeSettingsViewData Runtime { get; }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
