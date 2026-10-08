using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Conversation;
using UiharuMind.Shared.Controls;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 输入框旁的「新回复自动朗读」：接了朗读才有；点一下改的是会话角色卡上的开关并落盘，图标随之换
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ComposerAutoReadTests
{
    [Fact]
    public void Toggle_FlipsTheSessionCharactersSwitch() => HeadlessUi.Run(() =>
    {
        ConversationViewModel silent = new(new RecordingMessageService());
        Assert.False(silent.CanToggleAutoReadAloud); //没接朗读：不显示

        ReadAloudService readAloud = new(_ => new FakeAudioProvider(), new RecordingAudioPlayer(), new RecordingMessageService());
        ConversationViewModel vm = new(new RecordingMessageService(), readAloud);
        CharacterData character = CharacterManager.Instance.GetCharacterData(vm.NewSessionCharacterId);
        bool before = character.AutoReadAloud;
        Window window = new() { Width = 900, Height = 600, Content = new ConversationView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.True(vm.CanToggleAutoReadAloud);
            Assert.Equal(before, vm.AutoReadAloud);

            vm.ToggleAutoReadAloudCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(!before, character.AutoReadAloud);
            Assert.Equal(!before, vm.AutoReadAloud);
            string shown = !before ? "volume-2" : "volume-x";
            Assert.Contains(window.GetVisualDescendants().OfType<ThemedIconButton>(),
                x => x.IconName == shown && x.IsEffectivelyVisible);
            Capture(window, "composer-auto-read");
        }
        finally
        {
            if (character.AutoReadAloud != before) vm.ToggleAutoReadAloudCommand.Execute(null); //单例角色，别带给别的测试
            window.Close();
        }
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
}
