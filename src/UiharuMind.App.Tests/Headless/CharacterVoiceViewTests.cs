using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Characters.Voice;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 角色编辑里的朗读声音一节：真实模板下各行排得开；跟默认时没有音色行，选了模型才出来，不听语气的给提示
/// </summary>
[Collection(HeadlessCollection.Name)]
public class CharacterVoiceViewTests
{
    [Fact]
    public void Section_ShowsVoiceRowOnlyForAChosenModel()
    {
        HeadlessUi.Run(() =>
        {
            CharacterVoiceSource vox = new(AudioCppProvider.LocalId, "voxcpm-0.5b-q8_0", "VoxCPM", "voxcpm-0.5b-q8_0",
                ["小樱", "draw-2"], false, true);
            using CharacterVoiceViewData voice = new(null, _ => { },
                [CharacterVoiceSource.Default, vox], (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
                new ReadAloudService(_ => new FakeAudioProvider(), new RecordingAudioPlayer(), new RecordingMessageService()),
                () => "", 1f);
            Window window = new() { Width = 820, Height = 420, Content = new CharacterVoiceView(voice) };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(voice.CanChooseVoice);
            Capture(window, "character-voice-default");

            voice.SelectedSource = vox;
            voice.Voice.Text = "小樱";
            voice.HasOwnSpeed = true;
            voice.Speed = 0.9;
            voice.Instruction = "温柔、带点笑意";
            Dispatcher.UIThread.RunJobs();
            Assert.True(voice.IsInstructionIgnored);
            Capture(window, "character-voice-model");
            window.Close();
        });
    }

    private static void Capture(Window window, string name)
    {
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        window.UpdateLayout();
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
                             ?? throw new InvalidOperationException("空帧");
        frame.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
