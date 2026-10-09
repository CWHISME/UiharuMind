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
public sealed record ImageDrawActions(Action<string> Copy, Func<string, Task> SaveAs, Action<string> Pin,
    Action<string> OpenFolder);

/// <summary>
/// 画图窗口：只填提示词、挑一个生图模型，画好的存进档案的图片目录（以后的图库从这里读）。
/// 选了谁就是谁，不回退——这里试的就是它
/// </summary>
public partial class ImageDrawViewData : ObservableObject, IDisposable
{
    private readonly ImageDrawGenerator _generate;
    private readonly ImageDrawActions _actions;
    private readonly string _outputDirectory;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) }; //远程模型没有进度，报个用时
    private readonly Stopwatch _elapsed = new();
    private CancellationTokenSource? _running;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _prompt = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private ImageModelInfo? _selectedModel;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _isGenerating;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private Bitmap? _image;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultPath;

    /// <param name="models">可选的生图模型</param>
    /// <param name="selected">默认选中的；null 选第一个</param>
    /// <param name="generate">出图</param>
    /// <param name="actions">画好之后的动作</param>
    /// <param name="outputDirectory">画好的图存哪</param>
    public ImageDrawViewData(IReadOnlyList<ImageModelInfo> models, ImageModelInfo? selected,
        ImageDrawGenerator generate, ImageDrawActions actions, string outputDirectory)
    {
        Models = models;
        _selectedModel = selected != null && models.Contains(selected) ? selected : models.FirstOrDefault();
        _generate = generate;
        _actions = actions;
        _outputDirectory = outputDirectory;
        _statusText = models.Count == 0 ? Loc.Text(LangKey.ImageDrawNoModel) : "";
        _clock.Tick += (_, _) => StatusText = Loc.Text(LangKey.ImageDrawWaiting, (int)_elapsed.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// 可选的生图模型
    /// </summary>
    public IReadOnlyList<ImageModelInfo> Models { get; }

    /// <summary>
    /// 画好了一张
    /// </summary>
    public bool HasResult => ResultPath != null;

    public void Dispose()
    {
        _clock.Stop();
        _running?.Cancel();
        Image?.Dispose();
    }

    private bool CanGenerate() => !IsGenerating && SelectedModel != null && !string.IsNullOrWhiteSpace(Prompt);

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task Generate()
    {
        ImageModelInfo model = SelectedModel!;
        IsGenerating = true;
        ResultPath = null;
        _elapsed.Restart();
        _running = new CancellationTokenSource();
        StatusText = Loc.Text(LangKey.ImageDrawWaiting, 0);
        if (!model.IsLocal) _clock.Start();
        try
        {
            // 在界面线程上建，进度回调就回到界面线程
            Progress<ImageProgress> progress = new(OnProgress);
            ImageGenerationReport report = await _generate(model, new ImageRequest(Prompt.Trim(), [], null),
                progress, _running.Token);
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
        StatusText = progress.Stage switch
        {
            EImageStage.Starting => Loc.Text(LangKey.ImageDrawStarting),
            EImageStage.Queued => Loc.Text(LangKey.ImageDrawQueued),
            _ when progress.TotalSteps > 0 => Loc.Text(LangKey.ImageDrawStep, progress.Step, progress.TotalSteps),
            _ => StatusText
        };
        if (progress.Preview == null) return;
        using MemoryStream stream = new(progress.Preview);
        ReplaceImage(new Bitmap(stream));
    }

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
