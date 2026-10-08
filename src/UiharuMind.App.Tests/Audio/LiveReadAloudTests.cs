using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 边生成边念：正在流的回复攒够一句就念、收尾时念完剩下的，落盘后不再整条重念；
/// 没在看、角色没开、回放来的不边生成边念；流完被摘掉的空气泡不卡住后面的
/// </summary>
public class LiveReadAloudTests
{
    private readonly FakeAudioProvider _tts = new();
    private readonly ReadAloudService _service;
    private readonly ObservableCollection<ConversationItemBase> _items = [];
    private readonly ConversationReadAloud _readAloud;
    private bool _displayed = true;

    public LiveReadAloudTests()
    {
        _service = new ReadAloudService(_ => _tts, new InstantPlayer(), new RecordingMessageService());
        Dictionary<string, CharacterData> characters = new()
        {
            ["alice"] = new CharacterData { CharacterId = "alice", AutoReadAloud = true, Speech = new SpeechStyle { Voice = "a" } },
            ["quiet"] = new CharacterData { CharacterId = "quiet" }
        };
        _readAloud = new ConversationReadAloud(_items, _service,
            new CharacterSpeechStyles(id => characters.GetValueOrDefault(id), _ => true, _ => true), () => _displayed);
    }

    [Fact]
    public async Task StreamingReply_IsReadSentenceBySentence_AndNotAgainWhenItLands()
    {
        TextConversationItem reply = Streaming("alice");
        _items.Add(reply);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_tts.Requests);

        reply.Message = "Hello there. How are";
        await WaitFor(() => _tts.Requests.Count == 1);
        reply.Message = "Hello there. How are you? I am";
        await WaitFor(() => _tts.Requests.Count == 2);
        reply.Message = "Hello there. How are you? I am fine";
        reply.IsDone = true;
        await WaitFor(() => _tts.Requests.Count == 3 && _service.State == EReadAloudState.Idle);
        Assert.Equal([("Hello there.", "a"), ("How are you?", "a"), ("I am fine", "a")],
            _tts.Requests.Select(x => (x.Text, x.Voice)));

        _readAloud.ReadNew([Assistant("Hello there. How are you? I am fine")], "alice");
        _readAloud.ReadNew([Assistant("Something new.")], "alice"); //别处来的照念
        await WaitFor(() => _tts.Requests.Count == 4);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("Something new.", _tts.Requests[^1].Text);
        Assert.Equal(4, _tts.Requests.Count);
    }

    [Fact]
    public async Task NotDisplayed_QuietCharacter_OrReplayedBubble_IsNotReadLive()
    {
        _displayed = false;
        TextConversationItem hidden = Streaming("alice");
        _items.Add(hidden);
        _displayed = true;
        TextConversationItem quiet = Streaming("quiet");
        _items.Add(quiet);
        _items.Add(new TextConversationItem(false) { CharacterId = "alice", Message = "Replayed.", IsDone = true });

        foreach (TextConversationItem item in new[] { hidden, quiet })
        {
            item.Message = "Some words. More";
            item.IsDone = true;
        }

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(_tts.Requests);
    }

    [Fact]
    public async Task RemovedBlankBubble_DoesNotHoldUpTheQueue()
    {
        TextConversationItem blank = Streaming("alice");
        _items.Add(blank);
        _items.Remove(blank); //流完整条是空白的气泡会被摘掉，不会收尾

        _readAloud.ReadNew([Assistant("After the tool.")], "alice");
        await WaitFor(() => _tts.Requests.Count == 1);
        Assert.Equal("After the tool.", _tts.Requests[0].Text);
    }

    private static TextConversationItem Streaming(string characterId) =>
        new(false) { CharacterId = characterId, IsDone = false };

    private static ChatMessage Assistant(string text) => new(ChatRole.Assistant, text);

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class InstantPlayer : IAudioPlayer
    {
        public bool IsPlaying => false;

        public Task<bool> PlayAsync(AudioSynthesisResult audio, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public void Stop()
        {
        }
    }
}
