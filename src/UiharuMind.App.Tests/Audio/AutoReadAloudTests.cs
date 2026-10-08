using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 新回复自动朗读：只念开了的角色、按各自的声音、连着几条按顺序念；群里按每条的发言人；
/// 用户、旁白、化身代发不念；被停后排着的不再念；关掉自动朗读只停自动念的；别处在念就等它念完；
/// 智能体开口排进同一队、用角色声音配它给的语气，没念完如实回报
/// </summary>
public class AutoReadAloudTests
{
    private readonly FakeAudioProvider _tts = new();
    private readonly ControlledPlayer _player = new();
    private readonly ReadAloudService _service;
    private readonly ObservableCollection<ConversationItemBase> _items = [];
    private readonly ConversationReadAloud _readAloud;

    public AutoReadAloudTests()
    {
        _service = new ReadAloudService(_ => _tts, _player, new RecordingMessageService());
        Dictionary<string, CharacterData> characters = new()
        {
            ["alice"] = new CharacterData { CharacterId = "alice", AutoReadAloud = true, Speech = new SpeechStyle { Voice = "a" } },
            ["bob"] = new CharacterData { CharacterId = "bob", AutoReadAloud = true, Speech = new SpeechStyle { Voice = "b" } },
            ["quiet"] = new CharacterData { CharacterId = "quiet" }
        };
        _readAloud = new ConversationReadAloud(_items, _service,
            new CharacterSpeechStyles(id => characters.GetValueOrDefault(id), _ => true, _ => true));
    }

    [Fact]
    public async Task ReadsRepliesOfCharactersThatAskForIt_InOrder_InTheirVoices()
    {
        ChatMessage narration = Assistant("Once upon a time.");
        narration.AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Narration] = true };
        ChatMessage avatar = Assistant("Posted for the user.");
        ChatMessageAnnotations.MarkGroupAvatarPost(avatar, "avatar-session");
        ChatMessage bobInGroup = Assistant("Bob here.");
        ChatMessageAnnotations.MarkGroupPost(bobInGroup, "bob", "bob-session");
        TextConversationItem bubble = new(false) { Message = "First.", IsDone = true, SourceMessage = Assistant("First.") };
        _items.Add(bubble);

        _readAloud.ReadNew([bubble.SourceMessage!, new ChatMessage(ChatRole.User, "Hi."), narration, avatar, bobInGroup], "alice");
        await _player.Started.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(bubble.IsReadingAloud); //气泡配上了就显示「在念」
        _player.FinishAll = true;
        _player.Release();
        await WaitFor(() => _tts.Requests.Count == 2 && _service.State == EReadAloudState.Idle);

        Assert.Equal([("First.", "a"), ("Bob here.", "b")], _tts.Requests.Select(x => (x.Text, x.Voice)));
    }

    [Fact]
    public async Task QuietCharacter_IsNotRead()
    {
        _readAloud.ReadNew([Assistant("Hello.")], "quiet");
        _readAloud.ReadNew([Assistant("Hello.")], null);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_tts.Requests);
    }

    [Fact]
    public async Task Stopping_DropsTheRest()
    {
        _readAloud.ReadNew([Assistant("One."), Assistant("Two."), Assistant("Three.")], "alice");
        await _player.Started.WaitAsync(TimeSpan.FromSeconds(5));

        _service.Stop();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Single(_tts.Requests);
    }

    [Fact]
    public async Task TurningAutoReadOff_StopsTheAutoReading_ButNotAManualOne()
    {
        _readAloud.ReadNew([Assistant("One."), Assistant("Two.")], "alice");
        await _player.Started.WaitAsync(TimeSpan.FromSeconds(5));

        _readAloud.StopAuto();
        await WaitFor(() => _service.State == EReadAloudState.Idle);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(_tts.Requests); //第二条没再念

        TextConversationItem manual = new(true) { Message = "Mine.", IsDone = true };
        _items.Add(manual);
        Task reading = manual.ReadAloudCommand.ExecuteAsync(null);
        await WaitFor(() => _service.State != EReadAloudState.Idle);
        _readAloud.StopAuto();
        Assert.Same(manual, _service.Owner); //手点的不动
        _service.Stop();
        await reading.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AgentSpeech_UsesTheCharactersVoiceWithItsOwnTone_AndQueuesBehindAutoRead()
    {
        _readAloud.ReadNew([Assistant("Reply.")], "alice");
        Task<bool> spoken = _readAloud.SpeakAsync(new SpeechRequest("Psst.", "whispering"), "alice");
        Task<bool> plain = _readAloud.SpeakAsync(new SpeechRequest("Hello."), "quiet");
        _player.FinishAll = true;
        _player.Release();

        Assert.True(await spoken.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await plain.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal([("Reply.", "a", null), ("Psst.", "a", "whispering"), ("Hello.", null, null)],
            _tts.Requests.Select(x => (x.Text, x.Voice, x.Instruction)));
    }

    [Fact]
    public async Task AgentSpeech_StoppedOrDropped_ReportsNotHeard()
    {
        Task<bool> first = _readAloud.SpeakAsync(new SpeechRequest("One."), "alice");
        Task<bool> second = _readAloud.SpeakAsync(new SpeechRequest("Two."), "alice");
        await _player.Started.WaitAsync(TimeSpan.FromSeconds(5));

        _service.Stop();

        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5))); //排着的一并作废，智能体不会干等
    }

    [Fact]
    public async Task WaitsForAManualReadingToFinish()
    {
        TextConversationItem manual = new(true) { Message = "Mine.", IsDone = true };
        _items.Add(manual);
        Task reading = manual.ReadAloudCommand.ExecuteAsync(null);
        await _player.Started.WaitAsync(TimeSpan.FromSeconds(5));

        _readAloud.ReadNew([Assistant("Reply.")], "alice");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Single(_tts.Requests); //没把手点的那段打断

        _player.FinishAll = true;
        _player.Release();
        await reading.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitFor(() => _tts.Requests.Count == 2);
        Assert.Equal("Reply.", _tts.Requests[1].Text);
    }

    private static ChatMessage Assistant(string text) => new(ChatRole.Assistant, text);

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    /// <summary>
    /// 放到被放行为止；放行后（或设了 FinishAll）都直接放完
    /// </summary>
    private sealed class ControlledPlayer : IAudioPlayer
    {
        private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public bool FinishAll { get; set; }

        public bool IsPlaying => false;

        public void Release() => _gate.TrySetResult();

        public async Task<bool> PlayAsync(AudioSynthesisResult audio, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            if (!FinishAll) await _gate.Task.WaitAsync(cancellationToken);
            return true;
        }

        public void Stop()
        {
        }
    }
}
