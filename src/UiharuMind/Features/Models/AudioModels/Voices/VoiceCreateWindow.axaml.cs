using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.AudioModels.Voices;

/// <summary>
/// 添加音色对话框；后台代码只管开窗、选文件，逻辑在 <see cref="VoiceCreateViewData"/>
/// </summary>
public partial class VoiceCreateWindow : Window
{
    /// <summary>构造窗口（经 <see cref="ShowWindow"/> 打开）</summary>
    public VoiceCreateWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as VoiceCreateViewData)?.Close();
    }

    /// <summary>
    /// 打开添加音色对话框
    /// </summary>
    /// <param name="owner">父窗口</param>
    /// <param name="context">要用到的服务</param>
    /// <returns>存好的音色；取消为 null</returns>
    public static Task<VoiceSample?> ShowWindow(Window owner, VoiceCreateContext context)
    {
        VoiceCreateWindow window = new() { DataContext = new VoiceCreateViewData(context) };
        return window.ShowDialog<VoiceSample?>(owner);
    }

    /// <summary>
    /// 用共享的语音服务备齐添加音色要的东西
    /// </summary>
    /// <param name="library">音色库</param>
    /// <param name="audio">语音服务</param>
    /// <param name="messages">提示</param>
    /// <param name="drawModels">能抽卡的模型</param>
    /// <param name="hasAsr">本机有识别模型（能把参考音频自动识别成文字）</param>
    /// <returns>上下文</returns>
    public static VoiceCreateContext CreateContext(VoiceLibrary library, AudioServices audio, IMessageService messages,
        IReadOnlyList<VoiceDrawModel> drawModels, bool hasAsr)
    {
        AudioCppSettingConfig settings = AudioCppEngine.Shared.Settings;
        return new VoiceCreateContext(library, audio.Player, audio.Recorder, messages, drawModels,
            (model, seed, token) => audio.LocalProvider().DrawVoiceAsync(model, seed, token),
            hasAsr
                ? async (wav, token) => (await audio.LocalProvider()
                    .TranscribeAsync(new AudioTranscriptionRequest(new MemoryStream(wav, false), EAudioFormat.Wav), token)).Text
                : null,
            path => SoundFlowAudioFile.Read(path, VoiceCreateContext.SampleRate, VoiceCreateContext.MaxImportDuration),
            (model, voice) =>
            {
                settings.TtsVoices[model] = voice;
                settings.Save();
            },
            action => Dispatcher.UIThread.Post(action));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VoiceCreateViewData viewData && viewData.Save() is { } sample) Close(sample);
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VoiceCreateViewData viewData) return;
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Text(LangKey.VoiceImportPick),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Text(LangKey.VoiceImportFileType))
                {
                    Patterns = SoundFlowAudioFile.Extensions.Select(x => "*." + x).ToList()
                }
            ]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await viewData.ImportAsync(path);
    }
}
