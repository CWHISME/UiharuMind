using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 正在流的一条回复，按句交给朗读：攒够一个完整的句子就交出去，回复收尾时交出剩下的。
/// 在界面线程用（气泡的正文在界面线程按节拍刷新）
/// </summary>
internal sealed class LiveSpeechSource : IDisposable
{
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _consumed; //已经交出去的原文长度
    private bool _abandoned;

    /// <param name="item">正在流的助手气泡</param>
    public LiveSpeechSource(TextConversationItem item)
    {
        Item = item;
        item.PropertyChanged += OnItemChanged;
    }

    /// <summary>
    /// 正在流的气泡
    /// </summary>
    public TextConversationItem Item { get; }

    /// <summary>
    /// 不念了（气泡被摘掉、会话清空）：在等的立刻结束，剩下的不交
    /// </summary>
    public void Abandon()
    {
        _abandoned = true;
        _changed.TrySetResult();
    }

    public void Dispose()
    {
        Item.PropertyChanged -= OnItemChanged;
        Abandon();
    }

    /// <summary>
    /// 一段段交出可念的文字（已整理、已切段）；回复收尾或被放弃时结束
    /// </summary>
    /// <param name="cancellationToken">朗读被停</param>
    /// <returns>段</returns>
    public async IAsyncEnumerable<string> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!_abandoned)
        {
            Task changed = _changed.Task;
            string text = Item.Message;
            bool done = Item.IsDone;
            int ready = done ? text.Length : SpeechText.CompleteLength(text, _consumed);
            if (ready > _consumed)
            {
                string piece = text[_consumed..ready];
                _consumed = ready;
                foreach (string chunk in SpeechText.Split(SpeechText.Clean(piece))) yield return chunk;
                continue;
            }

            if (done) yield break;
            await changed.WaitAsync(cancellationToken);
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TextConversationItem.Message) or nameof(TextConversationItem.IsDone))) return;
        TaskCompletionSource changed = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
    }
}
