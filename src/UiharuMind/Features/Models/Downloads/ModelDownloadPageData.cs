using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Features.Models.Downloads.Audio;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 「获取模型」页签：顶部切分类（GGUF 仓库 / 语音模型），左栏列表、右栏详情由分类提供，底部下载区共用。
/// 第一次选中页签才建出来，网络请求也从那时才发
/// </summary>
public partial class ModelDownloadPageData : ObservableObject
{
    private readonly ModelDownloadContext _context;
    private bool _isActive; //页签选中过：之后切分类立刻加载

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private IModelMarket _selectedMarket;

    public ModelDownloadPageData(ModelDownloadContext context)
    {
        _context = context;
        Gguf = new GgufMarketData(context);
        Audio = new AudioMarketData(context);
        Markets = [Gguf, Audio];
        _selectedMarket = Gguf;
        Gguf.PropertyChanged += OnMarketPropertyChanged;
        Audio.PropertyChanged += OnMarketPropertyChanged;
    }

    /// <summary>
    /// GGUF 仓库（llama.cpp 用）
    /// </summary>
    public GgufMarketData Gguf { get; }

    /// <summary>
    /// 语音模型（audio.cpp 用）
    /// </summary>
    public AudioMarketData Audio { get; }

    /// <summary>
    /// 全部分类
    /// </summary>
    public IReadOnlyList<IModelMarket> Markets { get; }

    /// <summary>
    /// 下载区
    /// </summary>
    public DownloadQueueViewData Queue => _context.QueueView;

    /// <summary>
    /// 当前分类右栏有详情
    /// </summary>
    public bool HasDetail => SelectedMarket.HasDetail;

    /// <summary>
    /// 页签被选中时调：交给当前分类首次加载或按新设置重来
    /// </summary>
    public void Activate()
    {
        _isActive = true;
        SelectedMarket.Activate();
    }

    /// <summary>
    /// 切到 GGUF 仓库并预填搜索（还没选中页签时只预填，选中时再搜）
    /// </summary>
    /// <param name="search">搜索词</param>
    public void ShowSearch(string search)
    {
        SelectedMarket = Gguf;
        Gguf.CloseDetail();
        Gguf.SearchText = search;
    }

    /// <summary>
    /// 切到语音模型
    /// </summary>
    public void ShowAudio() => SelectedMarket = Audio;

    partial void OnSelectedMarketChanged(IModelMarket value)
    {
        if (_isActive) value.Activate();
    }

    [RelayCommand]
    private void CloseDetail() => SelectedMarket.CloseDetail();

    private void OnMarketPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == SelectedMarket && e.PropertyName == nameof(IModelMarket.HasDetail))
            OnPropertyChanged(nameof(HasDetail));
    }
}
