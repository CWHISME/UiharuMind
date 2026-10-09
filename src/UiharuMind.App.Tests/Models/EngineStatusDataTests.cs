using UiharuMind.Core.AI.Engines;
using UiharuMind.Features.Models;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Models;

/// <summary>
/// 本地引擎状态条：跟着进程状态写说明，启动时带几成，起不来说原因，释放走各引擎自己的停法
/// </summary>
public class EngineStatusDataTests
{
    private static readonly EngineStatusTexts Texts = new(LangKey.ImageEngineStatusStarting,
        LangKey.ImageEngineStatusStopped, LangKey.ImageEngineStatusFailed);

    [Fact]
    public void Text_FollowsTheRuntimeState()
    {
        FakeRuntime runtime = new();
        EngineStatusData status = new(runtime, Texts, () => "running!");
        Assert.Equal(Loc.Text(LangKey.ImageEngineStatusStopped), status.Text);
        Assert.False(status.IsRunning);

        runtime.State = EEngineRunState.Starting;
        status.Refresh();
        Assert.Equal(Loc.Text(LangKey.ImageEngineStatusStarting), status.Text);

        runtime.LoadProgress = 0.42f;
        status.Refresh();
        Assert.EndsWith(" 42%", status.Text);

        runtime.State = EEngineRunState.Running;
        status.Refresh();
        Assert.Equal("running!", status.Text);
        Assert.True(status.IsRunning);

        runtime.Usage = new EngineUsage(5L << 30, 12.4);
        status.Refresh();
        Assert.Equal($"running! · {Loc.Text(LangKey.EngineStatusUsage, "5.0", 12)}", status.Text);

        runtime.State = EEngineRunState.Failed;
        runtime.LastError = new LocalServerException("out of VRAM");
        status.Refresh();
        Assert.Equal(Loc.Text(LangKey.ImageEngineStatusFailed, "out of VRAM"), status.Text);
    }

    [Fact]
    public void Release_UsesTheGivenStop_OrStopsTheRuntime()
    {
        FakeRuntime runtime = new() { State = EEngineRunState.Running };
        int custom = 0;
        new EngineStatusData(runtime, Texts, () => "", () => custom++).ReleaseCommand.Execute(null);
        Assert.Equal(1, custom);
        Assert.Equal(0, runtime.Stops);

        new EngineStatusData(runtime, Texts, () => "").ReleaseCommand.Execute(null);
        Assert.Equal(1, runtime.Stops);
    }

    private sealed class FakeRuntime : IEngineRuntime
    {
        public EEngineRunState State { get; set; }
        public LocalServerException? LastError { get; set; }
        public float LoadProgress { get; set; }
        public EngineUsage? Usage { get; set; }
        public EngineUsage? SampleUsage() => State == EEngineRunState.Running ? Usage : null;
        public int Stops { get; private set; }
        public event Action? StateChanged;
        public event Action? LoadProgressChanged;

        public void Stop()
        {
            Stops++;
            State = EEngineRunState.Stopped;
            StateChanged?.Invoke();
            LoadProgressChanged?.Invoke();
        }
    }
}
