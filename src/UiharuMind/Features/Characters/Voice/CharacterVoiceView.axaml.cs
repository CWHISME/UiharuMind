using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Characters.Voice;

/// <summary>
/// 角色的朗读声音一节。数据上下文是 <see cref="CharacterDraft"/>；里面绑的那份按草稿现建，草稿换了或离开界面就丢
/// </summary>
public partial class CharacterVoiceView : UserControl
{
    private CharacterVoiceViewData? _voice;
    private readonly bool _isFixed; //无头测试自备数据，不按草稿重建

    public CharacterVoiceView()
    {
        InitializeComponent();
    }

    /// <param name="voice">现成的数据（无头测试用）</param>
    public CharacterVoiceView(CharacterVoiceViewData voice) : this()
    {
        _isFixed = true;
        Body.DataContext = voice;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_voice == null) Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Release();
    }

    // 音色库、在线服务可能刚在别处改过，每次进来都现取一份
    private void Rebuild()
    {
        if (_isFixed) return;
        Release();
        if (DataContext is not CharacterDraft draft || App.Services?.GetService<AudioServices>() is not { } audio) return;

        AudioRouter router = audio.Router();
        _voice = new CharacterVoiceViewData(draft.Speech, style => draft.Speech = style,
            CharacterVoiceSource.All(AudioCppEngine.Shared, router.Services(EAudioCapability.Tts), VoiceLibrary.Shared),
            (source, token) => ListVoicesAsync(audio, router, source, token), audio.ReadAloud,
            () => SampleText(draft), AudioSettingConfig.Current.ReadAloudSpeed);
        Body.DataContext = _voice;
    }

    private void Release()
    {
        if (_isFixed) return;
        _voice?.Dispose();
        _voice = null;
        Body.DataContext = null;
    }

    private static async Task<IReadOnlyList<string>> ListVoicesAsync(AudioServices audio, AudioRouter router,
        CharacterVoiceSource source, CancellationToken token)
    {
        if (source.IsLocal) return await audio.LocalProvider().GetVoicesAsync(source.Model!, token);
        IReadOnlyList<string> voices = await router.Resolve(EAudioCapability.Tts, source.Provider).GetVoicesAsync(token);
        return EdgeTtsProvider.SortVoices(voices, LocalizationManager.Instance.CurrentCulture.TwoLetterISOLanguageName);
    }

    // 念开场白的头两句最像这个角色；没有开场白就念通用的试听句。{{char}} 换成名字，{{user}} 念不出来就去掉
    private static string SampleText(CharacterDraft draft)
    {
        string greeting = Regex.Replace(draft.FirstGreeting ?? "", @"\{\{\$?char\}\}", draft.Name ?? "");
        greeting = Regex.Replace(greeting, @"\{\{\$?user\}\}", "");
        IReadOnlyList<string> chunks = SpeechText.Split(SpeechText.Clean(greeting));
        return chunks.Count > 0 ? string.Join("", chunks.Take(2)) : Loc.Text(LangKey.AudioPreviewText);
    }
}
