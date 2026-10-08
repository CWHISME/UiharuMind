using System.Collections.ObjectModel;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 朗读：逐句合成、边合成边播；再点同一条是停；被别的播放打断就不再往下念；失败翻成人话；
/// 会话里正在念的那条气泡按钮变「停止」
/// </summary>
public class ReadAloudServiceTests
{
    private readonly RecordingMessageService _messages = new();
    private readonly FakeAudioProvider _tts = new();
    private readonly GatedAudioPlayer _player = new();

    [Fact]
    public async Task ReadsSentenceBySentence_WithDetectedLanguage_ThenGoesIdle()
    {
        ReadAloudService service = new(() => _tts, _player, _messages);
        object owner = new();
        List<EReadAloudState> states = [];
        service.StateChanged += () => states.Add(service.State);
        _player.AutoFinish = true;

        await service.ToggleAsync(owner, "## 标题\n你好。" + string.Concat(Enumerable.Repeat("这是第二段里比较长的一句话。", 8)));

        Assert.True(_tts.Requests.Count >= 2);
        Assert.Equal("标题", _tts.Requests[0].Text.Split('\n')[0]); //Markdown 记号不念
        Assert.All(_tts.Requests, x => Assert.Equal("zh", x.Language));
        Assert.Equal(_tts.Requests.Count, _player.Played);
        Assert.Equal([EReadAloudState.Preparing, EReadAloudState.Speaking, EReadAloudState.Idle], states.Distinct());
        Assert.Null(service.Owner);
    }

    [Fact]
    public async Task ToggleSameOwner_Stops()
    {
        ReadAloudService service = new(() => _tts, _player, _messages);
        object owner = new();

        Task reading = service.ToggleAsync(owner, "Hello there. How are you today?");
        await _player.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EReadAloudState.Speaking, service.State);
        Assert.Same(owner, service.Owner);

        await service.ToggleAsync(owner, "Hello there. How are you today?");
        await reading.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(EReadAloudState.Idle, service.State);
        Assert.Equal(0, _player.Played); //没放完就停了
    }

    [Fact]
    public async Task PreviewWhileReading_StopsTheReadingAtOnce_AndUsesItsModelAndVoice()
    {
        ReadAloudService service = new(() => _tts, _player, _messages);
        object message = new();
        object preview = new();

        Task reading = service.ToggleAsync(message, "第一句。第二句。第三句。" + new string('长', 200));
        await _player.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _player.Reset();
        Task previewing = service.ToggleAsync(preview, "Hello.", new ReadAloudOptions("pocket", "alba"));
        await reading.WaitAsync(TimeSpan.FromSeconds(5)); //不排队：消息朗读当场停
        await _player.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(preview, service.Owner);
        AudioSynthesisRequest last = _tts.Requests[^1];
        Assert.Equal(("pocket", "alba"), (last.Model, last.Voice));

        await service.ToggleAsync(preview, "Hello."); //再点一次是停
        await previewing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EReadAloudState.Idle, service.State);
    }

    [Fact]
    public async Task InterruptedByAnotherPlayback_DoesNotReadOn()
    {
        ReadAloudService service = new(() => _tts, _player, _messages);
        _player.Interrupt = true; //第一句就被别的播放（如试听）顶掉

        await service.ToggleAsync(new object(), "One. Two. Three. " + new string('x', 300));

        Assert.Equal(EReadAloudState.Idle, service.State);
        Assert.Equal(1, _player.Calls);
    }

    [Fact]
    public async Task Failure_IsExplained_AndEmptyTextIsNoticed()
    {
        ReadAloudService service = new(() => _tts, _player, _messages);
        _tts.Error = new AudioModelMissingException(EAudioCppTask.Tts);

        await service.ToggleAsync(new object(), "Hello.");
        await service.ToggleAsync(new object(), "```\ncode only\n```");

        Assert.Equal(2, _messages.Notifications.Count);
        Assert.DoesNotContain("Exception", _messages.Notifications[0].Message);
        Assert.Equal(EReadAloudState.Idle, service.State);
    }

    [Fact]
    public async Task Conversation_MarksTheBubbleBeingRead()
    {
        ReadAloudService service = new(() => _tts, _player, _messages);
        ObservableCollection<ConversationItemBase> items = [];
        using ConversationReadAloud readAloud = new(items, service);
        TextConversationItem first = ConversationItemFactory.CreateNarration(new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.Assistant, "First message."));
        TextConversationItem second = ConversationItemFactory.CreateNarration(new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.Assistant, "Second message."));
        items.Add(first);
        items.Add(second);
        Assert.True(first.CanReadAloud);

        Task reading = first.ReadAloudCommand.ExecuteAsync(null);
        await _player.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first.IsReadingAloud);
        Assert.False(second.IsReadingAloud);

        _player.Reset();
        Task next = second.ReadAloudCommand.ExecuteAsync(null); //念另一条：前一条按钮复原
        await _player.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await reading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(first.IsReadingAloud);
        Assert.True(second.IsReadingAloud);

        service.Stop();
        await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(second.IsReadingAloud);
    }

    /// <summary>
    /// 放到被放行或被取消为止；可设成立刻放完、或一上来就被打断
    /// </summary>
    private sealed class GatedAudioPlayer : IAudioPlayer
    {
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool AutoFinish { get; set; }

        public bool Interrupt { get; set; }

        public int Played { get; private set; }

        public int Calls { get; private set; }

        public bool IsPlaying => false;

        public void Reset() => Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> PlayAsync(AudioSynthesisResult audio, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            if (Interrupt) return false;
            if (!AutoFinish) await Task.Delay(Timeout.Infinite, cancellationToken);
            Played++;
            return true;
        }

        public void Stop()
        {
        }
    }
}
