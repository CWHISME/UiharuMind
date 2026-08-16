using System.Threading;
using System.Threading.Tasks;

namespace UiharuMind.Shared.Services;

public enum MessageSeverity
{
    Information,
    Success,
    Warning,
    Error
}

/// <summary>
/// 三态确认的结果。用在「改了还没保存，却要走开」这类去留问题上——
/// 两态的 <see cref="IMessageService.ConfirmAsync"/> 表达不了「先别走」。
/// </summary>
public enum EConfirmChoice
{
    /// <summary>确认（保存后继续）</summary>
    Yes,

    /// <summary>否定（放弃改动后继续）</summary>
    No,

    /// <summary>取消整个操作（留在原地）。关掉弹窗等同于此</summary>
    Cancel
}

public interface IMessageService
{
    Task ShowInfoAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default);

    Task ShowWarningAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default);

    Task ShowErrorAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 三态确认：是 / 否 / 取消
    /// </summary>
    /// <param name="message">正文</param>
    /// <param name="title">标题；为空取默认</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>用户的选择；直接关掉弹窗视为 <see cref="EConfirmChoice.Cancel"/></returns>
    Task<EConfirmChoice> ConfirmWithCancelAsync(
        string message,
        string? title = null,
        CancellationToken cancellationToken = default);

    void ShowNotification(
        string message,
        string? title = null,
        MessageSeverity severity = MessageSeverity.Information);
}
