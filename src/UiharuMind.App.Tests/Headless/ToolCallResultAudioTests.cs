using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 配音卡片：结果里的草稿目录简写按会话口径展开、文件没了不显示、别的工具不认；
/// 卡上放音走朗读那一路（再点是停、状态映回卡片），收起时也能放
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ToolCallResultAudioTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-tool-audio-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AgentPathResolver Paths => new(Path.Combine(_root, "workspace"), Path.Combine(_root, "draft"));

    private string WriteWav(string relativePath)
    {
        string path = Path.Combine(_root, "draft", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, WavEncoder.Encode(new float[2400], 24000, 1));
        return path;
    }

    private static ToolCallItem Finished(string toolName, string result) => new()
    {
        ToolName = toolName, IsRunning = false, IsSuccess = true, ResultText = result,
    };

    [Fact]
    public void VoiceOver_ResolvesDraftShorthand_OthersAndMissingFilesShowNothing()
    {
        string wav = WriteWav("audio/line-1.wav");
        ToolCallItem item = Finished(SynthesizeSpeechTool.ToolName, "Saved: $DRAFT/audio/line-1.wav\n1.0 s, voice of Alice.");
        ToolCallItem missing = Finished(SynthesizeSpeechTool.ToolName, "Saved: $DRAFT/audio/gone.wav");
        ToolCallItem otherTool = Finished(ImageGenerationTool.ToolName, "Saved: $DRAFT/audio/line-1.wav");

        item.LoadResultAudio(Paths);
        missing.LoadResultAudio(Paths);
        otherTool.LoadResultAudio(Paths);

        Assert.Equal(wav, item.ResultAudioPath);
        Assert.Equal("line-1.wav", item.ResultAudioName);
        Assert.False(missing.HasResultAudio);
        Assert.False(otherTool.HasResultAudio);
    }

    [Fact]
    public void PlayingFromTheCard_GoesThroughReadAloud_AndShowsOnTheCard() => HeadlessUi.Run(() =>
    {
        string wav = WriteWav("audio/line-1.wav");
        HoldingPlayer player = new();
        ReadAloudService service = new(_ => new FakeAudioProvider(), player, new RecordingMessageService());
        ObservableCollection<ConversationItemBase> items = [];
        using ConversationReadAloud readAloud = new(items, service);
        ToolCallItem item = Finished(SynthesizeSpeechTool.ToolName, "Saved: $DRAFT/audio/line-1.wav");
        item.LoadResultAudio(Paths);
        items.Add(item);
        Window window = new() { Width = 640, Height = 200, Content = new ToolCallCardView { DataContext = item } };
        window.Show();

        item.PlayResultAudioCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(item.IsReadingAloud);
        Assert.Equal(new FileInfo(wav).Length, player.LastBytes);
        Capture(window, "tool-card-audio");

        item.PlayResultAudioCommand.Execute(null); //再点是停
        Dispatcher.UIThread.RunJobs();
        Assert.False(item.IsReadingAloud);
        window.Close();
    });

    private static void Capture(Window window, string name)
    {
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        window.UpdateLayout();
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
                             ?? throw new InvalidOperationException("空帧");
        frame.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    // 一直放到被停为止，记下放的字节数
    private sealed class HoldingPlayer : IAudioPlayer
    {
        public long LastBytes { get; private set; }

        public bool IsPlaying => false;

        public async Task<bool> PlayAsync(AudioSynthesisResult audio, CancellationToken cancellationToken)
        {
            LastBytes = audio.Audio.Length;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return true;
        }

        public void Stop()
        {
        }
    }
}
