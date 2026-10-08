using Avalonia.Input;
using SharpHook.Data;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.Core;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Clipboard;

namespace UiharuMind.App.Tests.Audio;

/// <summary>
/// 在别的应用里听写：本应用的输入框优先；开了开关、本应用不在前台才送别的应用；
/// 粘贴时剪贴板借用后原样还原、两下都不进历史；悬浮提示只在录给别的应用时出现
/// </summary>
public class OtherAppDictationTests
{
    private readonly FakeAudioRecorder _recorder = new();
    private readonly FakeAudioProvider _asr = new() { TranscribedText = "你好" };
    private readonly DictationService _service;

    public OtherAppDictationTests()
    {
        _service = new DictationService(_recorder, () => _asr, new RecordingMessageService(), action => action());
    }

    [Fact]
    public async Task HoldKey_FallsBackToTheForegroundApp_OnlyWhenEnabledAndThisAppIsNotInFront()
    {
        bool ownAppActive = true;
        List<string> pasted = [];
        ForegroundAppDictationTarget otherApps = new(() => ownAppActive, text =>
        {
            pasted.Add(text);
            return Task.CompletedTask;
        });
        TimeSpan now = TimeSpan.Zero;
        SettingConfig settings = new() { DictationHoldKey = "VcRightAlt", DictationHoldEnabled = true };
        using DictationHotkey hotkey = new(_service, () => settings, action => action(), () => now, otherApps);

        hotkey.OnKeyDown(KeyCode.VcRightAlt);
        Assert.Equal(EDictationState.Idle, _service.State); //开关没开

        settings.DictationInOtherApps = true;
        hotkey.OnKeyDown(KeyCode.VcRightAlt);
        Assert.Equal(EDictationState.Idle, _service.State); //本应用在前台：不往自己身上粘

        ownAppActive = false;
        hotkey.OnKeyDown(KeyCode.VcRightAlt);
        Assert.Same(otherApps, _service.Target);
        now += TimeSpan.FromSeconds(2);
        hotkey.OnKeyUp(KeyCode.VcRightAlt);
        await WaitFor(() => pasted.Count == 1);
        Assert.Equal("你好", pasted[0]);

        FakeTarget box = new();
        _service.Focus(box);
        hotkey.OnKeyDown(KeyCode.VcRightAlt);
        Assert.Same(box, _service.Target); //本应用有可用的输入框就送它
        _service.Cancel();
    }

    [Fact]
    public async Task Paste_BorrowsTheClipboard_RestoresIt_AndKeepsHistoryOut()
    {
        FakePasteboard pasteboard = new() { Text = "用户原来复制的" };
        List<TimeSpan> ignored = [];
        string? atPaste = null;
        ForegroundPaster paster = new(pasteboard, ignored.Add, () =>
        {
            atPaste = pasteboard.Text;
            return Task.CompletedTask;
        });

        await paster.PasteAsync("听写的字");

        Assert.Equal("听写的字", atPaste);
        Assert.Equal("用户原来复制的", pasteboard.Text);
        Assert.Equal(2, ignored.Count); //放进去、还原各挡一次
        Assert.All(ignored, x => Assert.True(x >= TimeSpan.FromSeconds(1)));

        // 按粘贴键失败也要还原
        ForegroundPaster failing = new(pasteboard, _ => { }, () => throw new InvalidOperationException("no permission"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.PasteAsync("x"));
        Assert.Equal("用户原来复制的", pasteboard.Text);
    }

    [Fact]
    public void Indicator_ShowsOnlyForTheWatchedTarget()
    {
        ForegroundAppDictationTarget otherApps = new(() => false, _ => Task.CompletedTask);
        using DictationIndicatorViewData indicator = new(_service, otherApps);
        int changes = 0;
        indicator.ActiveChanged += () => changes++;

        _service.Start(new FakeTarget());
        Assert.False(indicator.IsActive);
        _service.Cancel();

        _service.Start(otherApps);
        Assert.True(indicator.IsRecording);
        _service.Cancel();
        Assert.False(indicator.IsActive);
        Assert.Equal(2, changes);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class FakeTarget : IDictationTarget
    {
        public bool IsAvailable => true;

        public void Insert(string text)
        {
        }
    }

    private sealed class FakePasteboard : IPasteboard
    {
        public string? Text { get; set; }

        public Task<DataTransfer?> SaveAsync()
        {
            if (Text == null) return Task.FromResult<DataTransfer?>(null);
            DataTransfer saved = new();
            saved.Add(DataTransferItem.CreateText(Text));
            return Task.FromResult<DataTransfer?>(saved);
        }

        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }

        public Task RestoreAsync(DataTransfer? saved)
        {
            Text = saved?.Items[0].TryGetRaw(DataFormat.Text) as string;
            return Task.CompletedTask;
        }
    }
}
