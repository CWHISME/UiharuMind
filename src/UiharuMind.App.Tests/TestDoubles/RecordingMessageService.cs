using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.TestDoubles;

/// <summary>
/// 消息服务替身：确认框按预设回答，问过什么、弹过什么都记下来。不弹任何窗口，同步完成
/// </summary>
internal sealed class RecordingMessageService : IMessageService
{
    /// <summary>两态确认框的回答</summary>
    public bool ConfirmResult { get; set; } = true;

    /// <summary>三态确认框的回答</summary>
    public EConfirmChoice ConfirmChoice { get; set; } = EConfirmChoice.Yes;

    /// <summary>问过的确认（两态与三态），按先后</summary>
    public List<string> Confirms { get; } = [];

    /// <summary>弹过的通知，按先后</summary>
    public List<(string Message, MessageSeverity Severity)> Notifications { get; } = [];

    /// <summary>弹过的信息 / 警告 / 错误对话框正文，按先后</summary>
    public List<string> Dialogs { get; } = [];

    /// <summary>问过几次确认</summary>
    public int ConfirmCount => Confirms.Count;

    /// <summary>最近一次确认的正文；没问过为 null</summary>
    public string? LastConfirm => Confirms.LastOrDefault();

    public Task ShowInfoAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
        Record(message);

    public Task ShowWarningAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
        Record(message);

    public Task ShowErrorAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
        Record(message);

    public Task<bool> ConfirmAsync(string message, string? title = null, CancellationToken cancellationToken = default)
    {
        Confirms.Add(message);
        return Task.FromResult(ConfirmResult);
    }

    public Task<EConfirmChoice> ConfirmWithCancelAsync(string message, string? title = null,
        CancellationToken cancellationToken = default)
    {
        Confirms.Add(message);
        return Task.FromResult(ConfirmChoice);
    }

    public void ShowNotification(string message, string? title = null,
        MessageSeverity severity = MessageSeverity.Information, TimeSpan? duration = null) =>
        Notifications.Add((message, severity));

    private Task Record(string message)
    {
        Dialogs.Add(message);
        return Task.CompletedTask;
    }
}
