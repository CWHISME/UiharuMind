using System.Threading;
using System.Threading.Tasks;

namespace UiharuMind.Services;

public enum MessageSeverity
{
    Information,
    Success,
    Warning,
    Error
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

    void ShowNotification(
        string message,
        string? title = null,
        MessageSeverity severity = MessageSeverity.Information);
}
