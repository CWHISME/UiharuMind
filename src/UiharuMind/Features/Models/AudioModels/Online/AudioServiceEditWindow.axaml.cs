using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Online;

/// <summary>
/// 在线语音服务的新建、编辑对话框；后台代码只管开窗与调系统，表单逻辑在 <see cref="AudioServiceEditViewData"/>
/// </summary>
public partial class AudioServiceEditWindow : Window
{
    /// <summary>构造窗口（经 <see cref="ShowWindow"/> 打开）</summary>
    public AudioServiceEditWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as AudioServiceEditViewData)?.Close();
    }

    /// <summary>
    /// 打开新建/编辑在线语音服务的对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="source">要编辑的服务；null 为新建</param>
    /// <param name="takenNames">其他服务已用的名字</param>
    /// <param name="player">表单里试听出声</param>
    /// <param name="messages">失败提示</param>
    /// <returns>确认时为新实例，取消为 null</returns>
    public static Task<AudioServiceInfo?> ShowWindow(Window owner, AudioServiceInfo? source,
        IReadOnlyCollection<string> takenNames, IAudioPlayer player, IMessageService messages)
    {
        // 沿用密钥的候选取远程对话模型那张表：那边是唯一存密钥的地方
        AudioServiceEditWindow window = new()
        {
            DataContext = new AudioServiceEditViewData(source, takenNames,
                RemoteModelSettingConfig.Current.ModelInfos.Values.ToList(),
                token => AudioServiceProviderFactory.Create(new AudioServiceInfo { Dialect = EAudioDialect.EdgeTts })
                    .GetVoicesAsync(token),
                (service, token) => PreviewAsync(service, player, token), messages)
        };
        return window.ShowDialog<AudioServiceInfo?>(owner);
    }

    // 用表单现在的样子建一个临时 provider 念一句；不进路由，不影响正在用的
    private static async Task PreviewAsync(AudioServiceInfo service, IAudioPlayer player, CancellationToken token)
    {
        IAudioProvider provider = AudioServiceProviderFactory.Create(service);
        try
        {
            AudioSynthesisResult result = await provider.SynthesizeAsync(
                new AudioSynthesisRequest(Loc.Text(LangKey.AudioPreviewText),
                    Language: LocalizationManager.Instance.CurrentCulture.TwoLetterISOLanguageName), token);
            await using Stream audio = result.Audio;
            await player.PlayAsync(result, token);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioServiceEditViewData { CanConfirm: true } viewData) return;
        Close(viewData.BuildResult());
    }

    private void OpenWebsite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioServiceEditViewData { HasWebsite: true } viewData) return;
        _ = Launcher.LaunchUriAsync(new Uri(viewData.WebsiteUrl));
    }
}
