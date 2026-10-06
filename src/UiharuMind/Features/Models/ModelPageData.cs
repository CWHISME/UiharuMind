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
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Features.Models.ImageModels;
using UiharuMind.Features.Settings;
using UiharuMind.Core.Core.DownloadHelper;

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Features.Models;

public partial class ModelPageData : PageDataBase
{
    private readonly IMessageService _messageService;
    private readonly Func<ModelPageData, ModelDownloadPageData> _createDownloads;
    private readonly DownloadQueue? _downloadQueueSource; //null 走全局队列
    private bool _isSyncingModelPath; //切页对齐路径期间抑制提示
    // public string? Title { get; set; } = "Model Viewer";
    // public string? ModelPrefix { get; set; } = "Local models folder: ";
    [ObservableProperty] private string? _modelPath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isListDataReady;
    private bool _isListSchedulePending; //防抖：同一拍里多次 OnEnable 只排一次点亮

    public ObservableCollection<ModelRunningData> ModelSources => App.ModelService.ModelSources;

    /// <summary>
    /// 切页首帧只出页面骨架，条目列表推迟一拍再挂载。
    /// 原因：ListBox 在切页同步布局里会一次性物化全部条目（实测 16 条 ~80ms），
    /// 直接绑 <see cref="ModelSources"/> 会把这笔账压进点击路径；
    /// 先绑空集合让骨架 40ms 内出画，下一拍再换全集合并让引擎自然布局。
    /// </summary>
    public IEnumerable<ModelRunningData> CurrentItems =>
        IsListDataReady ? ModelSources : Array.Empty<ModelRunningData>();

    partial void OnIsListDataReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentItems));
    }
    
    public ModelPageData() : this(App.Services.GetRequiredService<IMessageService>())
    {
    }

    /// <param name="messageService">提示与通知</param>
    /// <param name="createDownloads">建「获取模型」页签的数据，默认走真实模型源与全局队列</param>
    /// <param name="downloadQueue">下载队列，默认全局</param>
    public ModelPageData(IMessageService messageService,
        Func<ModelPageData, ModelDownloadPageData>? createDownloads = null, DownloadQueue? downloadQueue = null)
    {
        _messageService = messageService;
        _createDownloads = createDownloads ?? CreateDefaultDownloads;
        ImageModels = new ImageModelListViewData(messageService,
            (source, takenNames) => ImageModelEditWindow.ShowWindow(UIManager.GetRootWindow(), source, takenNames));
        _downloadQueueSource = downloadQueue;
    }

    private const int DownloadsTabIndex = 2;

    /// <summary>生图模型页签（ADR 0052）</summary>
    public ImageModelListViewData ImageModels { get; }

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

    /// <summary>当前页签：0 对话模型，1 生图模型，2 获取模型</summary>
    [ObservableProperty] private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == DownloadsTabIndex) EnsureDownloads().Activate();
    }

    private ModelDownloadPageData EnsureDownloads() => Downloads ??= _createDownloads(this);

    private static ModelDownloadPageData CreateDefaultDownloads(ModelPageData page)
    {
        return new ModelDownloadPageData(new ModelDownloadContext(page._messageService, page.EnsureDownloadQueue(),
            () => App.ModelService.LoadModelList(),
            () => UIManager.ShowWindow<SettingsWindow>(window => window.ShowDownloadSourceSettings()))
        {
            UseModel = name => App.ModelService.LoadModelWithRiskConfirmationAsync(name),
            HasCurrentModel = () => App.ModelService.CurModelRunningData != null
        });
    }

    /// <summary>
    /// 切到生图模型页签。从 Agent 设置页跳过来时用
    /// </summary>
    public void ShowImageModels()
    {
        SelectedTabIndex = 1;
        ImageModels.Refresh();
    }

    /// <summary>
    /// 切到「获取模型」页签并预填搜索。从服务页嵌入模型的空状态跳过来时用
    /// </summary>
    /// <param name="search">预填的搜索词</param>
    public void ShowDownloads(string search)
    {
        ModelDownloadPageData downloads = EnsureDownloads();
        downloads.CloseDetailCommand.Execute(null);
        downloads.SearchText = search;
        SelectedTabIndex = DownloadsTabIndex;
    }

    [RelayCommand]
    private async Task OpenChangeModelPath()
    {
        ModelSettingConfig.Current.LocalModelPath = await App.FilesService.OpenSelectFolderAsync(ModelSettingConfig.Current.LocalModelPath)!;
        ModelPath = ModelSettingConfig.Current.LocalModelPath;
        ModelSettingConfig.Current.Save();
    }

    [RelayCommand]
    private void OpenModelFolder()
    {
        App.FilesService.OpenFolder(ModelSettingConfig.Current.LocalModelPath);
    }


    [RelayCommand]
    private void OpenSelectModelFolder(string path)
    {
        App.FilesService.OpenFolder(Path.GetDirectoryName(path) ?? path);
    }

    [RelayCommand]
    private async Task RefreshSelectModelInfo(string path)
    {
        await App.ModelService.LoadModelList();
        _messageService.ShowNotification(Loc.Text(LangKey.ModelInfoReloaded, path));
    }

    [RelayCommand]
    private async Task OpenSelectModelInfo(string path)
    {
        GGufModelInfo? info = ModelSources
            .Select(model => model.ModelInfo)
            .OfType<GGufModelInfo>()
            .FirstOrDefault(model => model.ModelPath == path);
        if (info == null)
        {
            _messageService.ShowNotification(path);
            return;
        }

        string message = string.Join(Environment.NewLine, new[]
        {
            $"Name: {info.DisplayName}",
            $"Architecture: {info.Architecture}",
            $"Size: {info.SizeLabel}",
            $"Context: {info.ContextLength:N0}",
            $"Embedding: {info.EmbeddingLength:N0}",
            $"Layers: {info.LayerCount}",
            $"Heads: {info.AttentionHeadCount} / KV {info.AttentionHeadCountKv}",
            $"File: {info.ModelPath}"
        }.Where(line => !line.EndsWith(": ", StringComparison.Ordinal)));

        await _messageService.ShowInfoAsync(message, info.ModelName);
    }

    [RelayCommand]
    private async Task CreateRemoteModel(string? name)
    {
        RemoteModelInfo? info = null;
        if (name != null) LlmManager.Instance.TryGetRemoteModelInfo(name, out info);
        var model = await CreateRemoteLlmModelWindow.ShowWindow(UIManager.GetRootWindow(), info);
        if (model != null)
        {
            LlmManager.Instance.AddRemoteModel(model);
            LoadModels();
        }
    }

    [RelayCommand]
    private async Task DeleteRemoteModel(string name)
    {
        if (await _messageService.ConfirmAsync(Loc.Text(LangKey.RemoteModelDeleteConfirm, name)))
        {
            LlmManager.Instance.DeleteRemoteModel(name);
            LoadModels();
        }
    }

    /// <summary>
    /// 随包内置的模型不给删
    /// </summary>
    /// <param name="model">模型</param>
    /// <returns>可删返回 true</returns>
    public static bool CanDeleteLocalModel(ModelRunningData model)
    {
        if (model.IsRemoteModel || string.IsNullOrEmpty(model.ModelPath)) return false;
        string builtIn = Path.GetFullPath(ModelSettingConfig.Current.DefaultLocalModelPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return !Path.GetFullPath(model.ModelPath).StartsWith(builtIn, StringComparison.Ordinal);
    }

    [RelayCommand]
    private async Task DeleteLocalModel(ModelRunningData model)
    {
        if (!CanDeleteLocalModel(model)) return;
        if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.ModelDeleteFilesConfirm, model.ModelName, model.ModelPath)))
            return;

        try
        {
            // 跑着的先停掉：Windows 上被占用的文件删不掉
            if (model.IsRunning) App.ModelService.UnloadModel(model.ModelName);
            await Task.Run(() => LocalModelDeleter.Delete(model.ModelPath));
            ModelRuntimeSettingConfig.Current.SetOverrides(model.ModelName, null);
            ModelRuntimeSettingConfig.Current.Save();
            _messageService.ShowNotification(Loc.Text(LangKey.ModelDeleteFilesDone, model.ModelName),
                severity: MessageSeverity.Success);
        }
        catch (Exception e)
        {
            Log.Warning($"Delete model files failed: {model.ModelPath}, {e.Message}");
            _messageService.ShowNotification(Loc.Text(LangKey.ModelDeleteFilesFailed, e.Message),
                severity: MessageSeverity.Error);
        }

        LoadModels();
    }

    [RelayCommand]
    private void SetFavoriteModel(string? name)
    {
        if (name == null) return;
        bool isRemove = ModelSettingConfig.Current.IsFavorite(name);
        ModelSettingConfig.Current.ToggleFavorite(name);
        ModelSettingConfig.Current.Save();

        _messageService.ShowNotification(isRemove
            ? Loc.Text(LangKey.FavoriteRemoteModelDelTips, name)
            : Loc.Text(LangKey.FavoriteRemoteModelSetTips, name));
    }

    partial void OnModelPathChanged(string? value)
    {
        LoadModels();
        // 切页时的对齐不是用户改的路径,不该弹提示
        if (!_isSyncingModelPath) _messageService.ShowNotification(Loc.Text(LangKey.ModelListUpdated));
    }

    public override void OnEnable()
    {
        base.OnEnable();
        // 熔断会随时间恢复,回到这一页时重刷一次生图模型的状态
        ImageModels.Refresh();
        // 从设置页改完下载源回来要换源重搜
        if (SelectedTabIndex == DownloadsTabIndex) Downloads?.Activate();
        // 骨架先行：首帧先不亮列表，下一拍再挂全集合并让引擎出画——
        // 否则 ListBox 一次性物化会卡在点击路径上（见 CurrentItems 注释）
        if (!IsListDataReady && !_isListSchedulePending)
        {
            _isListSchedulePending = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    _isListSchedulePending = false;
                    IsListDataReady = true;
                    EnsureDownloadQueue();
                },
                DispatcherPriority.Background);
        }

        if (ModelPath == ModelSettingConfig.Current.LocalModelPath) return;

        _isSyncingModelPath = true;
        try
        {
            ModelPath = ModelSettingConfig.Current.LocalModelPath;
        }
        finally
        {
            _isSyncingModelPath = false;
        }
    }

    protected override Control CreateView => new ModelPage();

    // 原先是 async void：catch 之外再漏一个异常就是进程级崩溃。改成即发即忘，忙标志与日志交给作用域
    private void LoadModels()
    {
        _ = AsyncCommandScope.RunAsync(v => IsBusy = v, App.ModelService.LoadModelList);
    }

    private void UpdateModel(ModelRunningData model)
    {
        var index = ModelSources.IndexOf(model);
        if (index == -1) return;
        ModelSources[index] = model;
    }

}
