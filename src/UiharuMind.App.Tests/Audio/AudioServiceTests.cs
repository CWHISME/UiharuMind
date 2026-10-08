using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Models.AudioModels.Online;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 在线语音服务：表单按预设填好、自定义可选用途、编辑不动没改的字段；列表增删改、设默认与本地二选一、试听走指定服务
/// </summary>
public class AudioServiceTests
{
    private readonly RecordingMessageService _messages = new();
    private readonly List<AudioServiceInfo> _previewed = [];

    [Fact]
    public void NewForm_StartsWithEdge_NeedsNothing_ThenPresetFillsTheRest()
    {
        AudioServiceEditViewData form = Form(null);

        Assert.True(form.IsEdge);
        Assert.True(form.CanConfirm); //Edge 什么都不用填
        string language = UiharuMind.Shared.Services.LocalizationManager.Instance.CurrentCulture.TwoLetterISOLanguageName;
        Assert.Equal(3, form.Voice.Options.Count);
        Assert.StartsWith(language is "zh" or "ja" ? language : "en", form.Voice.Options[0]); //界面语言的排前面，其次英文
        AudioServiceInfo edge = form.BuildResult();
        Assert.Equal(EAudioDialect.EdgeTts, edge.Dialect);
        Assert.Equal("", edge.Voice); //留空按语种挑

        form.SelectedPreset = form.Presets.Single(x => x.Preset.Key == "openai-tts");
        Assert.True(form.IsCompatible);
        Assert.Equal("https://api.openai.com/v1", form.Endpoint);
        Assert.Equal("gpt-4o-mini-tts", form.ModelId.Text);
        Assert.Equal("alloy", form.Voice.Text);
        Assert.Equal(form.Presets.Single(x => x.Preset.Key == "openai-tts").DisplayName, form.Name); //名字跟着预设走
        Assert.False(form.CanChooseTask);

        form.ModelId.Text = "";
        Assert.True(form.HasModelIdError);
        Assert.False(form.CanConfirm);
    }

    [Fact]
    public void Custom_ChoosesTask_AsrHasNoVoice_AndCopiesKeyFromChatModel()
    {
        RemoteModelInfo chat = new() { Config = new RemoteAgnesModelConfig { ModelName = "agnes" }, ApiKey = "sk-chat" };
        AudioServiceEditViewData form = Form(null, chatModels: [chat]);
        form.SelectedPreset = form.Presets.Single(x => x.Preset.Key == AudioServicePreset.CustomKey);
        Assert.True(form.CanChooseTask);

        form.Endpoint = "https://api.agnes-ai.cn/v1"; //与那个对话模型同一主机
        form.ModelId.Text = "whisper";
        form.SelectedTask = form.TaskOptions.Single(x => x.Task == EAudioCapability.Asr);
        form.CopyKeyFromCommand.Execute(Assert.Single(form.CopySources));

        Assert.False(form.IsTts);
        AudioServiceInfo result = form.BuildResult();
        Assert.Equal(EAudioCapability.Asr, result.Task);
        Assert.Equal("sk-chat", result.ApiKey);
        Assert.Equal("", result.Voice);
    }

    [Fact]
    public async Task Edit_KeepsFieldsTheFormDoesNotShow_AndPreviewUsesTheDraft()
    {
        AudioServiceInfo source = new()
        {
            Name = "我的 OpenAI", Endpoint = "https://api.openai.com/v1", ModelId = "tts-1", Voice = "nova", TimeoutSeconds = 99
        };
        AudioServiceEditViewData form = Form(source, takenNames: ["Edge TTS"]);

        Assert.True(form.IsEditMode);
        Assert.Equal("openai-tts", form.SelectedPreset!.Preset.Key);
        Assert.Equal("nova", form.Voice.Text);

        form.Voice.Text = "onyx";
        await form.PreviewCommand.ExecuteAsync(null);
        Assert.Equal("onyx", Assert.Single(_previewed).Voice);

        form.Name = "Edge TTS";
        Assert.True(form.HasNameError);
        form.Name = "改名";
        AudioServiceInfo result = form.BuildResult();
        Assert.Equal(99, result.TimeoutSeconds);
        Assert.Equal("nova", source.Voice); //不就地改活实例
    }

    [Fact]
    public async Task Preview_ClickAgain_StopsAStuckPreview()
    {
        AudioServiceEditViewData form = new(null, [], [], _ => Task.FromResult<IReadOnlyList<string>>([]),
            (_, token) => Task.Delay(Timeout.Infinite, token), _messages);

        Task first = form.PreviewCommand.ExecuteAsync(null);
        Assert.True(form.IsPreviewing);
        await form.PreviewCommand.ExecuteAsync(null);
        await first.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.False(form.IsPreviewing);
        Assert.Empty(_messages.Notifications); //自己停的不算失败

        // 换服务商、换音色都停：正在念的已经不是表单现在的样子
        Task second = form.PreviewCommand.ExecuteAsync(null);
        form.SelectedPreset = form.Presets.Single(x => x.Preset.Key == "openai-tts");
        await second.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(form.IsPreviewing);

        Task third = form.PreviewCommand.ExecuteAsync(null);
        form.Voice.Text = "nova";
        await third.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(form.IsPreviewing);
    }

    [Fact]
    public async Task List_AddEditDelete_DefaultFollowsRename_AndPreviewTargetsTheService()
    {
        AudioServiceSettingConfig config = new();
        AudioSettingConfig settings = new();
        FakeAudioProvider local = new();
        FakeAudioProvider online = new() { IsLocal = false };
        using AudioRouter router = new(() => local, settings, config, _ => online);
        List<string?> resolved = [];
        ReadAloudService readAloud = new(id =>
        {
            resolved.Add(id);
            return router.Resolve(EAudioCapability.Tts, id);
        }, new RecordingAudioPlayer(), _messages);
        Queue<AudioServiceInfo?> edits = new();
        AudioServiceListViewData list = new(router, config, readAloud, _messages, (_, _) => Task.FromResult(edits.Dequeue()));

        edits.Enqueue(new AudioServiceInfo { Name = "Edge", Dialect = EAudioDialect.EdgeTts });
        await list.AddCommand.ExecuteAsync(null);
        AudioServiceItemData edge = Assert.Single(list.TtsServices);
        Assert.False(edge.IsDefault);

        list.SetDefaultCommand.Execute(edge);
        Assert.True(list.TtsServices[0].IsDefault);
        Assert.Equal("service:Edge", settings.PreferredTtsProviderId);

        edits.Enqueue(new AudioServiceInfo { Name = "微软", Dialect = EAudioDialect.EdgeTts });
        await list.EditCommand.ExecuteAsync(list.TtsServices[0]);
        Assert.Equal("service:微软", settings.PreferredTtsProviderId);
        Assert.True(list.TtsServices[0].IsDefault);

        await list.PreviewCommand.ExecuteAsync(list.TtsServices[0]);
        Assert.Equal(["service:微软"], resolved);
        Assert.Single(online.Requests);

        await list.DeleteCommand.ExecuteAsync(list.TtsServices[0]);
        Assert.Empty(list.TtsServices);
        Assert.Same(local, router.Resolve(EAudioCapability.Tts));
    }

    private AudioServiceEditViewData Form(AudioServiceInfo? source, IReadOnlyCollection<string>? takenNames = null,
        IReadOnlyList<RemoteModelInfo>? chatModels = null) =>
        new(source, takenNames ?? [], chatModels ?? [],
            _ => Task.FromResult<IReadOnlyList<string>>(["en-US-AriaNeural", "ja-JP-NanamiNeural", "zh-CN-XiaoxiaoNeural"]),
            (service, _) =>
            {
                _previewed.Add(service);
                return Task.CompletedTask;
            }, _messages);
}
