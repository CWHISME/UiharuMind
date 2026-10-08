using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.Core;
using UiharuMind.Features.Audio;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 输入框作为听写去向：插到光标处、有选中就替换，焦点登记成按住说话的去向；麦克风按钮点一下录、再点一下填字
/// </summary>
[Collection(HeadlessCollection.Name)]
public class DictationTextBoxTests
{
    [Fact]
    public void Insert_AtCaret_ReplacingSelection_AndButtonRoundTrip()
    {
        HeadlessUi.RunAsync(async () =>
        {
            FakeAudioProvider asr = new() { TranscribedText = "XY" };
            DictationService service = new(new FakeAudioRecorder(), () => asr, new RecordingMessageService(),
                action => Dispatcher.UIThread.Post(action));
            TextBox box = new() { Text = "abcdef" };
            Window window = new() { Content = box };
            window.Show();
            window.Activate();
            using TextBoxDictationTarget target = new(box, service);
            box.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(target, service.FocusedTarget); //获得焦点即登记

            box.CaretIndex = 3;
            box.SelectionStart = box.SelectionEnd = 3;
            target.Insert("好");
            Assert.Equal("abc好def", box.Text);
            Assert.Equal(4, box.CaretIndex);

            box.SelectionStart = 0;
            box.SelectionEnd = 3;
            target.Insert("Z");
            Assert.Equal("Z好def", box.Text);

            using ComposerDictationViewData button = new(service, target, () => new SettingConfig());
            await button.ToggleCommand.ExecuteAsync(null);
            Assert.True(button.IsRecording);
            await button.ToggleCommand.ExecuteAsync(null);
            Assert.True(button.IsIdle);
            Assert.Equal("Z XY好def", box.Text); //插在光标处（Z 后），英文接英文隔一个空格

            window.Close();
        });
    }
}
