using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 出一次图（指定模型、不回退，本地的带进度）
/// </summary>
public delegate Task<ImageGenerationReport> ImageDrawGenerator(ImageModelInfo model, ImageRequest request,
    IProgress<ImageProgress> progress, CancellationToken cancellationToken);

/// <summary>
/// 画好之后对那张图做的事（界面外壳的系统调用）
/// </summary>
/// <param name="Copy">复制到剪贴板</param>
/// <param name="SaveAs">另存为</param>
/// <param name="Pin">钉住预览</param>
/// <param name="OpenFolder">打开所在目录</param>
/// <param name="PickImages">挑几张参考图，返回路径；取消为空</param>
public sealed record ImageDrawActions(Action<string> Copy, Func<string, Task> SaveAs, Action<string> Pin,
    Action<string> OpenFolder, Func<Task<IReadOnlyList<string>>> PickImages);

/// <summary>
/// 出图比例的一个选项
/// </summary>
/// <param name="Value">比例；null 为自动（没参考图出方图，有参考图跟第一张的形状）</param>
/// <param name="Label">显示名</param>
public sealed record ImageRatioOption(ImageAspectRatio? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// 画图窗口：填提示词、挑一个生图模型，带参考图就是改图；画好的存进档案的图片目录（以后的图库从这里读）。
/// 选了谁就是谁，不回退——这里试的就是它
/// </summary>
public partial class ImageDrawViewData : ObservableObject, IDisposable
{
    private readonly ImageDrawGenerator _generate;
    private readonly ImageDrawActions _actions;
    private readonly string _outputDirectory;
    private readonly Func<IReadOnlyList<LocalMemoryHolder>> _memoryHolders;
    private readonly TimeSpan _releaseSettle;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) }; //远程模型没有进度，报个用时
    private readonly Stopwatch _elapsed = new();
    private CancellationTokenSource? _running;
    private (ImageModelInfo Model, ImageRequest Request)? _refused; //因为内存不够没起的那次，「仍然加载」照它再来
    private IReadOnlyList<LocalMemoryHolder> _holders = []; //那一刻在跑、可以先放掉的其他本地模型

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _prompt = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyPropertyChangedFor(nameof(CannotEditHint))]
    private ImageModelInfo? _selectedModel;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _isGenerating;

    [ObservableProperty] private ImageRatioOption _selectedRatio;
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasMemoryRisk))]
    private string? _memoryRiskText;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanReleaseOthers))]
    private string? _releaseOthersText;
    [ObservableProperty] private Bitmap? _image;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultPath;

    /// <param name="models">可选的生图模型</param>
    /// <param name="selected">默认选中的；null 选第一个</param>
    /// <param name="generate">出图</param>
    /// <param name="actions">画好之后的动作</param>
    /// <param name="outputDirectory">画好的图存哪</param>
    /// <param name="memoryHolders">此刻在跑的其他本地模型（内存不够时给「先放掉再加载」）；null 不给</param>
    /// <param name="releaseSettle">放掉之后等系统收回内存多久；null 为 2 秒</param>
    public ImageDrawViewData(IReadOnlyList<ImageModelInfo> models, ImageModelInfo? selected,
        ImageDrawGenerator generate, ImageDrawActions actions, string outputDirectory,
        Func<IReadOnlyList<LocalMemoryHolder>>? memoryHolders = null, TimeSpan? releaseSettle = null)
    {
        Models = models;
        _selectedModel = selected != null && models.Contains(selected) ? selected : models.FirstOrDefault();
        _generate = generate;
        _actions = actions;
        _outputDirectory = outputDirectory;
        _memoryHolders = memoryHolders ?? (() => []);
        _releaseSettle = releaseSettle ?? TimeSpan.FromSeconds(2);
        _statusText = models.Count == 0 ? Loc.Text(LangKey.ImageDrawNoModel) : "";
        _clock.Tick += (_, _) => StatusText = Loc.Text(LangKey.ImageDrawWaiting, (int)_elapsed.Elapsed.TotalSeconds);
        Ratios = [new ImageRatioOption(null, Loc.Text(LangKey.ImageDrawRatioAuto)),
            ..ImageAspectRatio.Supported.Select(r => new ImageRatioOption(r, r.ToString()))];
        _selectedRatio = Ratios[0];
        References.Changed += () =>
        {
            GenerateCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CannotEditHint));
        };
    }

    /// <summary>
    /// 可选的生图模型
    /// </summary>
    public IReadOnlyList<ImageModelInfo> Models { get; }

    /// <summary>
    /// 可选的出图比例：自动 + 各家都支持的那几种（宽高由引擎按模型基准边长换算）
    /// </summary>
    public IReadOnlyList<ImageRatioOption> Ratios { get; }

    /// <summary>
    /// 参考图：有就是改图
    /// </summary>
    public ImageDrawReferences References { get; } = new();

    /// <summary>
    /// 带了参考图、选的模型却不能改图时的提示；能出图为空串
    /// </summary>
    public string CannotEditHint => References.HasItems && SelectedModel is { SupportsEditing: false } model
        ? Loc.Text(LangKey.ImageDrawCannotEdit, model.Name)
        : "";

    /// <summary>
    /// 画好了一张
    /// </summary>
    public bool HasResult => ResultPath != null;

    /// <summary>
    /// 本地引擎因为此刻内存不够没起，等用户决定要不要照样加载
    /// </summary>
    public bool HasMemoryRisk => MemoryRiskText != null;

    /// <summary>
    /// 内存不够时，有别的本地模型在跑、可以先放掉
    /// </summary>
    public bool CanReleaseOthers => ReleaseOthersText != null;

    public void Dispose()
    {
        _clock.Stop();
        _running?.Cancel();
        Image?.Dispose();
        References.Dispose();
    }

    private bool CanGenerate() => !IsGenerating && SelectedModel != null && !string.IsNullOrWhiteSpace(Prompt)
                                  && (!References.HasItems || SelectedModel.SupportsEditing);

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task Generate() => RunAsync(SelectedModel!, new ImageRequest(Prompt.Trim(), [], SelectedRatio.Value));

    // 内存不够的那次，用户看过提示后照样加载
    [RelayCommand]
    private Task GenerateAnyway() => _refused is { } refused && !IsGenerating
        ? RunAsync(refused.Model, refused.Request with { AllowMemoryRisk = true })
        : Task.CompletedTask;

    // 先放掉别的本地模型（对话模型、语音引擎），等系统收回内存，再照常加载（仍按此刻内存查）
    [RelayCommand]
    private async Task ReleaseOthersAndGenerate()
    {
        if (_refused is not { } refused || IsGenerating) return;
        foreach (LocalMemoryHolder holder in _holders) holder.Release();
        MemoryRiskText = null;
        ReleaseOthersText = null;
        StatusText = Loc.Text(LangKey.ImageDrawReleasing);
        await Task.Delay(_releaseSettle);
        await RunAsync(refused.Model, refused.Request);
    }

    private async Task RunAsync(ImageModelInfo model, ImageRequest request)
    {
        _refused = null;
        MemoryRiskText = null;
        ReleaseOthersText = null;
        IsGenerating = true;
        ResultPath = null;
        _elapsed.Restart();
        _running = new CancellationTokenSource();
        StatusText = Loc.Text(LangKey.ImageDrawWaiting, 0);
        if (!model.IsLocal) _clock.Start();
        try
        {
            // 参考图出图时才读；「仍然加载」重试的那次已经带着
            if (request.Images.Count == 0 && References.HasItems)
            {
                (List<ImageInput> inputs, string? unreadable) = await References.LoadAsync();
                if (unreadable != null)
                {
                    StatusText = Loc.Text(LangKey.ImageDrawReferenceUnreadable, unreadable);
                    return;
                }

                request = request with { Images = inputs };
            }

            // 在界面线程上建，进度回调就回到界面线程
            Progress<ImageProgress> progress = new(OnProgress);
            ImageGenerationReport report = await _generate(model, request, progress, _running.Token);
            // 本地引擎此刻内存不够没起：窗口里说清楚差多少，用户自己决定要不要硬起
            if (report.Failure?.MemoryShortfall is { } shortfall)
            {
                _refused = (model, request);
                _holders = _memoryHolders();
                ReleaseOthersText = _holders.Count == 0
                    ? null
                    : Loc.Text(LangKey.ImageDrawReleaseOthers, string.Join("、", _holders.Select(x => x.Name)));
                MemoryRiskText = Loc.Text(LangKey.ImageDrawMemoryRisk, ToGb(shortfall.EstimatedBytes),
                    ToGb(shortfall.AvailableBytes));
                StatusText = "";
                return;
            }

            if (report.Failure != null || report.Images.Count == 0)
            {
                StatusText = Loc.Text(LangKey.ImageDrawFailed, report.Failure?.Message ?? "");
                return;
            }

            string path = await Task.Run(() => Save(report.Images[0]));
            ShowImage(path);
            ResultPath = path;
            StatusText = Loc.Text(LangKey.ImageDrawDone, (int)_elapsed.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.Text(LangKey.ImageDrawStopped);
        }
        finally
        {
            _clock.Stop();
            _running.Dispose();
            _running = null;
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private async Task AddReferences() => await References.AddAsync(await _actions.PickImages());

    // 拿画好的这张接着改：放到最前，它就是要改的那张
    [RelayCommand]
    private Task EditResult() => ResultPath != null ? References.AddAsync([ResultPath], asFirst: true) : Task.CompletedTask;

    // 在算的停不下来，取消会停掉本地引擎（下次重新加载）
    [RelayCommand]
    private void Stop() => _running?.Cancel();

    [RelayCommand]
    private void Copy()
    {
        if (ResultPath != null) _actions.Copy(ResultPath);
    }

    [RelayCommand]
    private Task SaveAs() => ResultPath != null ? _actions.SaveAs(ResultPath) : Task.CompletedTask;

    [RelayCommand]
    private void Pin()
    {
        if (ResultPath != null) _actions.Pin(ResultPath);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (ResultPath != null) _actions.OpenFolder(ResultPath);
    }

    private void OnProgress(ImageProgress progress)
    {
        StatusText = ImageProgressText.Describe(progress) ?? StatusText;
        if (progress.Preview == null) return;
        using MemoryStream stream = new(progress.Preview);
        ReplaceImage(new Bitmap(stream));
    }

    private static string ToGb(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("0.0");

    private void ShowImage(string path) => ReplaceImage(new Bitmap(path));

    private void ReplaceImage(Bitmap image)
    {
        Bitmap? previous = Image;
        Image = image;
        previous?.Dispose();
    }

    // 文件名：时间 + 提示词开头，看目录就知道画的什么
    private string Save(GeneratedImage image)
    {
        Directory.CreateDirectory(_outputDirectory);
        string stem = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Slug(Prompt)}";
        string path = Path.Combine(_outputDirectory, stem + ImageFormats.ExtensionOf(image.MediaType));
        File.WriteAllBytes(path, image.Bytes);
        return path;
    }

    private static string Slug(string prompt)
    {
        StringBuilder slug = new();
        foreach (char c in prompt.Trim())
        {
            if (slug.Length >= 24) break;
            slug.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        return slug.ToString().Trim('-') is { Length: > 0 } text ? text : "image";
    }
}
