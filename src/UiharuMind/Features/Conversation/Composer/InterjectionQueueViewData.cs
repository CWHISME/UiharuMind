using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Conversation.Composer;

/// <summary>
/// 已投入注入队列、模型还没消费的插话。它们不在时间轴上——位置要等消费那一刻才定——
/// 所以在输入区上方列出来，免得看着像没发出去。
///
/// 撤回即恢复成「还没发出去」：文字回输入框、附件放回盘上，并从执行者的注入队列里摘走
/// </summary>
public sealed partial class InterjectionQueueViewData
{
    private readonly Func<ICharacterRunner?> _runnerSource;
    private readonly Func<string> _readInput;
    private readonly Action<string> _writeInput;
    private readonly ObservableCollection<ConversationAttachment> _trayAttachments;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="runnerSource">当前会话的执行者（注入队列在它那里）</param>
    /// <param name="readInput">读输入框</param>
    /// <param name="writeInput">写输入框</param>
    /// <param name="trayAttachments">附件盘（撤回时附件放回这里）</param>
    public InterjectionQueueViewData(Func<ICharacterRunner?> runnerSource, Func<string> readInput,
        Action<string> writeInput, ObservableCollection<ConversationAttachment> trayAttachments)
    {
        _runnerSource = runnerSource;
        _readInput = readInput;
        _writeInput = writeInput;
        _trayAttachments = trayAttachments;
    }

    /// <summary>待发的插话，按投入顺序</summary>
    public ObservableCollection<PendingInterjectionViewData> Items { get; } = new();

    /// <summary>
    /// 投入注入队列，成功即挂一条待发提示。排不进去（没有会话、执行者还在装配、或不支持注入）时
    /// 文字还给输入框、附件放回盘上——静默吞掉就是「点了没反应」
    /// </summary>
    /// <param name="message">组装好的插话；没有会话时为 null</param>
    /// <param name="text">用户原话（提示条显示它，撤回时还给输入框）</param>
    /// <param name="attachments">随插话带的附件</param>
    /// <returns>是否投进去了</returns>
    public async Task<bool> TryInjectAsync(ChatMessage? message, string text, List<ConversationAttachment>? attachments)
    {
        if (message != null && _runnerSource() is { } runner && await runner.TryInjectAsync(new[] { message }))
        {
            Items.Add(new PendingInterjectionViewData(message, text, attachments));
            return true;
        }

        RestoreAttachments(attachments);
        _writeInput(text);
        return false;
    }

    /// <summary>
    /// 插的那句话被模型消费、画进时间轴了：待发提示撤掉
    /// </summary>
    /// <param name="message">被消费的那条（与投入时是同一个实例）</param>
    public void OnRendered(ChatMessage message)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Items[i].Message, message)) Items.RemoveAt(i);
        }
    }

    /// <summary>
    /// 一次性地把待发的插话全部撤掉（停止/一轮结束时的收尾）。
    /// 只清界面提示、不动队列的旧行为，就是「停止之后插话还遗留在那」的由来；
    /// 队列里那些不撤走，下次再跑会被模型突然消费，连提示都没有就冒出来。
    ///
    /// 没被模型消费的话还该属于用户：按原顺序回填输入框（已有内容则追加，不覆盖），
    /// 附件一并放回盘上——否则主动停止一次，刚打的字就没了。
    /// </summary>
    public async Task CancelAllAsync()
    {
        if (Items.Count == 0) return;

        string restored = string.Join("\n", Items.Select(x => x.Text));
        if (!string.IsNullOrEmpty(restored))
        {
            string input = _readInput();
            _writeInput(string.IsNullOrEmpty(input) ? restored : $"{input}\n{restored}");
        }

        foreach (PendingInterjectionViewData pending in Items) RestoreAttachments(pending.Attachments);

        ChatMessage[] messages = Items.Select(x => x.Message).ToArray();
        Items.Clear();
        await CancelInRunnerAsync(messages, "撤销待发插话失败");
    }

    /// <summary>
    /// 撤掉一条待发的插话：提示条先拿掉（不管队列里撤没撤成），再从注入队列里摘走——
    /// 撤不回来（已被模型消费）的那条此刻已经画进时间轴，提示条本也会由
    /// <see cref="OnRendered"/> 撤，这里幂等
    /// </summary>
    /// <param name="message">要撤的那条</param>
    [RelayCommand]
    private async Task Remove(ChatMessage? message)
    {
        if (message == null) return;

        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(Items[i].Message, message)) continue;

            PendingInterjectionViewData pending = Items[i];
            _writeInput(pending.Text);
            RestoreAttachments(pending.Attachments);
            Items.RemoveAt(i);
            break;
        }

        await CancelInRunnerAsync(new[] { message }, "撤销插话失败");
    }

    private void RestoreAttachments(List<ConversationAttachment>? attachments)
    {
        if (attachments == null) return;
        foreach (ConversationAttachment attachment in attachments) _trayAttachments.Add(attachment);
    }

    private async Task CancelInRunnerAsync(ChatMessage[] messages, string failureLog)
    {
        try
        {
            if (_runnerSource() is { } runner) await runner.CancelInjectionsAsync(messages);
        }
        catch (Exception e)
        {
            // 队列访问反射失败等:界面提示已撤,模型之后仍可能收到这句——最低限度是不能再让 UI 崩
            Log.Warning($"{failureLog}: {e.Message}");
        }
    }
}

/// <summary>
/// 输入区上方那一条待发的插话
/// </summary>
/// <param name="Message">投入注入队列的那个实例（与消费时流出来的是同一个，据此撤掉提示）</param>
/// <param name="Text">显示文本</param>
/// <param name="Attachments">这条插话携带的附件;撤回时放回盘上,插话才算完整撤回</param>
public sealed record PendingInterjectionViewData(
    ChatMessage Message, string Text, List<ConversationAttachment>? Attachments = null);
