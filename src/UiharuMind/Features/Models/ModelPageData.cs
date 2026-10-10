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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Features.Models.AudioModels.Online;
using UiharuMind.Features.Models.AudioModels.Options;
using UiharuMind.Features.Models.AudioModels.Voices;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Models.AudioModels;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Features.Models.Downloads.Audio;
using UiharuMind.Features.Models.Downloads.Image;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Models.ChatModels;
using UiharuMind.Features.Models.ImageModels;
using UiharuMind.Features.Settings;
using UiharuMind.Core.Core.DownloadHelper;

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Features.Models;

/// <summary>
/// 模型页：对话、生图、语音模型与获取模型四个页签的路由与组装，各页签的数据各管各的
/// </summary>
public partial class ModelPageData : PageDataBase
{
    private readonly IMessageService _messageService;
    private readonly Func<ModelPageData, ModelDownloadPageData> _createDownloads;
    private readonly Func<ModelPageData, AudioModelListViewData> _createAudioModels;
    private readonly AudioServices? _audio; //默认的语音模型页签用；测试自备页签数据时可不给
    private readonly DownloadQueue? _downloadQueueSource; //null 走全局队列

    public ModelPageData() : this(App.Services.GetRequiredService<IMessageService>(), audio: App.Services.GetRequiredService<AudioServices>())
    {
    }

    /// <param name="messageService">提示与通知</param>
    /// <param name="createDownloads">建「获取模型」页签的数据，默认走真实模型源与全局队列</param>
    /// <param name="downloadQueue">下载队列，默认全局</param>
    /// <param name="modelSources">对话模型列表，默认 App.ModelService 的</param>
    /// <param name="createAudioModels">建「语音模型」页签的数据，默认走共享的语音引擎</param>
    /// <param name="audio">默认的语音模型页签要用的语音服务</param>
    /// <param name="createImageActions">生图模型页签里本地模型用的动作，默认开真实窗口、删真实文件</param>
    public ModelPageData(IMessageService messageService,
        Func<ModelPageData, ModelDownloadPageData>? createDownloads = null, DownloadQueue? downloadQueue = null,
        ObservableCollection<ModelRunningData>? modelSources = null,
        Func<ModelPageData, AudioModelListViewData>? createAudioModels = null, AudioServices? audio = null,
        Func<ModelPageData, ImageModelListActions>? createImageActions = null)
    {
        _audio = audio;
        _messageService = messageService;
        // 测试自备模型列表时不接真实的 llama 进程
        EngineStatusData? chatEngine = modelSources == null
            ? ChatEngineStatus.Create(LlmManager.Instance,
                () => UIManager.ShowWindow<SettingsWindow>(window => window.ShowTextEngineSettings()))
            : null;
        ChatModels = new ChatModelListViewData(messageService, () => SelectedTabIndex = DownloadsTabIndex, modelSources,
            chatEngine);
        _createDownloads = createDownloads ?? CreateDefaultDownloads;
        ImageModels = new ImageModelListViewData(messageService,
            (source, takenNames) => ImageModelEditWindow.ShowWindow(UIManager.GetRootWindow(), source, takenNames),
            (createImageActions ?? CreateImageActions)(this));
        _downloadQueueSource = downloadQueue;
        _createAudioModels = createAudioModels ?? CreateDefaultAudioModels;
    }

    /// <summary>「语音模型」页签的序号</summary>
    public const int AudioModelsTabIndex = 2;

    /// <summary>
    /// 「生图模型」页签的序号
    /// </summary>
    public const int ImageModelsTabIndex = 1;

    /// <summary>「获取模型」页签的序号</summary>
    public const int DownloadsTabIndex = 3;

    /// <summary>对话模型页签</summary>
    public ChatModelListViewData ChatModels { get; }

    /// <summary>生图模型页签（ADR 0052）</summary>
    public ImageModelListViewData ImageModels { get; }

    /// <summary>「语音模型」页签：第一次选中才建（会碰语音引擎与它的设置），扫盘也在选中时</summary>
    [ObservableProperty] private AudioModelListViewData? _audioModels;

    /// <summary>
    /// 下载区（页签头的进行中数量也读它）。首帧之后才建：碰全局队列会连带初始化下载器，不该压在切页路径上
    /// </summary>
    [ObservableProperty] private DownloadQueueViewData? _downloadQueue;

    /// <summary>
    /// 取下载区，没建就现建
    /// </summary>
    /// <returns>下载区</returns>
    public DownloadQueueViewData EnsureDownloadQueue() => DownloadQueue ??=
        new DownloadQueueViewData(_downloadQueueSource ?? Core.Core.DownloadHelper.DownloadQueue.Shared);

    /// <summary>「获取模型」页签：第一次选中才建，模型页首开不发网络请求</summary>
    [ObservableProperty] private ModelDownloadPageData? _downloads;

    /// <summary>当前页签：0 对话模型，1 生图模型，2 语音模型，3 获取模型</summary>
    [ObservableProperty] private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == DownloadsTabIndex) EnsureDownloads().Activate();
        else if (value == AudioModelsTabIndex) _ = EnsureAudioModels().RefreshAsync();
    }

    private AudioModelListViewData EnsureAudioModels() => AudioModels ??= _createAudioModels(this);

    private ModelDownloadPageData EnsureDownloads() => Downloads ??= _createDownloads(this);

    private static AudioModelListViewData CreateDefaultAudioModels(ModelPageData page)
    {
        AudioServices audio = page._audio ?? throw new InvalidOperationException("No audio services for the voice models tab.");
        AudioCppEngine engine = AudioCppEngine.Shared;
        AudioModelListViewData? list = null;
        VoiceLibraryViewData? voices = engine.Voices is { } library
            ? new VoiceLibraryViewData(library, engine.Settings, page._messageService, audio.Player,
                () => VoiceCreateWindow.ShowWindow(UIManager.GetRootWindow(),
                    VoiceCreateWindow.CreateContext(library, audio, page._messageService, list?.DrawModels ?? [],
                        list?.HasAsrModels == true)),
                path => App.FilesService.OpenFolder(path), action => Dispatcher.UIThread.Post(action))
            : null;
        AudioServiceListViewData online = new(audio.Router(), AudioServiceSettingConfig.Current, audio.ReadAloud,
            page._messageService, (source, takenNames) => AudioServiceEditWindow.ShowWindow(UIManager.GetRootWindow(),
                source, takenNames, audio.Player, page._messageService));
        list = new AudioModelListViewData(page._messageService, engine, AudioCppPackageStore.Shared,
            (model, token) => audio.LocalProvider().GetVoicesAsync(model, token), audio.ReadAloud,
            path => App.FilesService.OpenFolder(path), page.ShowAudioDownloads,
            () => UIManager.ShowWindow<SettingsWindow>(window => window.ShowAudioEngineSettings()), voices, online,
            new AudioPreferencesViewData(AudioSettingConfig.Current),
            options => AudioModelOptionsWindow.ShowWindow(UIManager.GetRootWindow(), options));
        return list;
    }

    private static ModelDownloadPageData CreateDefaultDownloads(ModelPageData page)
    {
        ModelDownloadContext context = new(page._messageService, page.EnsureDownloadQueue(),
            () => App.ModelService.LoadModelList(),
            () => UIManager.ShowWindow<SettingsWindow>(window => window.ShowDownloadSourceSettings()));
        GgufMarketHooks gguf = new()
        {
            UseModel = name => App.ModelService.LoadModelWithRiskConfirmationAsync(name),
            HasCurrentModel = () => App.ModelService.CurModelRunningData != null
        };
        ImageMarketServices image = new(SdCppEngine.Shared.Distribution, LocalImageModelStore.Shared, ImageModelCatalog.BuiltIn,
            () => ImageModelSettingConfig.Current.Models, page.ImageModels.Append,
            model => ImageDrawWindow.ShowWindow(UIManager.GetRootWindow(), model),
            url => _ = UIManager.GetRootWindow().Launcher.LaunchUriAsync(new Uri(url)));
        return new ModelDownloadPageData(context,
        [
            new GgufMarketData(context, gguf), new AudioMarketData(context, AudioMarketServices.CreateShared()),
            new ImageMarketData(context, image)
        ]);
    }

    private static ImageModelListActions CreateImageActions(ModelPageData page) => new(
        (model, takenNames) => LocalImageModelEditWindow.ShowWindow(UIManager.GetRootWindow(), model, takenNames),
        model => ImageDrawWindow.ShowWindow(UIManager.GetRootWindow(), model),
        LocalImageModelStore.Shared.DeleteAsync,
        page.ShowImageDownloads,
        ImageEngineStatus.Create(SdCppEngine.Shared,
            () => UIManager.ShowWindow<SettingsWindow>(window => window.ShowImageEngineSettings())),
        LocalImageModelStore.Shared.IsInstalled,
        model => LocalImageModels.SizeOnDisk(model, LocalImageModels.ModelRoot),
        LocalImageModels.ModelRoot,
        path => App.FilesService.OpenFolder(path));

    /// <summary>
    /// 切到生图模型页签。从 Agent 设置页跳过来时用
    /// </summary>
    public void ShowImageModels()
    {
        SelectedTabIndex = ImageModelsTabIndex;
        ImageModels.Refresh();
    }

    /// <summary>
    /// 切到「获取模型」页签并预填搜索。从服务页嵌入模型的空状态跳过来时用
    /// </summary>
    /// <param name="search">预填的搜索词</param>
    public void ShowDownloads(string search)
    {
        EnsureDownloads().ShowSearch(search);
        SelectedTabIndex = DownloadsTabIndex;
    }

    /// <summary>
    /// 切到「语音模型」页签。从设置里的「本地模型（语音）」跳过来时用
    /// </summary>
    public void ShowAudioModels()
    {
        if (SelectedTabIndex == AudioModelsTabIndex) _ = EnsureAudioModels().RefreshAsync();
        else SelectedTabIndex = AudioModelsTabIndex;
    }

    /// <summary>
    /// 切到「获取模型」的生图分类
    /// </summary>
    public void ShowImageDownloads()
    {
        EnsureDownloads().Show<ImageMarketData>();
        SelectedTabIndex = DownloadsTabIndex;
    }

    /// <summary>
    /// 切到「获取模型」的语音分类
    /// </summary>
    public void ShowAudioDownloads()
    {
        EnsureDownloads().Show<AudioMarketData>();
        SelectedTabIndex = DownloadsTabIndex;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        // 熔断会随时间恢复,回到这一页时重刷一次生图模型的状态
        ImageModels.Refresh();
        // 从设置页改完下载源回来要换源重搜；语音模型可能在设置里装了引擎、换了默认
        if (SelectedTabIndex == DownloadsTabIndex) Downloads?.Activate();
        else if (SelectedTabIndex == AudioModelsTabIndex) _ = AudioModels?.RefreshAsync();
        ChatModels.Enable();
        // 下载区早建出来，页签头上才有在下的个数
        Dispatcher.UIThread.Post(() => EnsureDownloadQueue(), DispatcherPriority.Background);
    }

    protected override Control CreateView => new ModelPage();
}
