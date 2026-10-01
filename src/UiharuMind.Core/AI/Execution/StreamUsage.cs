using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 一次流式调用只报一份用量。用量是累计值，有的服务商（实测 SenseNova 上的 deepseek-flash）
/// 在收尾帧与末帧各报一次，下游逐条记账就翻倍。这里把用量从沿途的更新里摘掉，流尾只补发最后那一份
/// </summary>
public static class StreamUsage
{
    /// <summary>
    /// 只保留最后一份用量；其余内容按原顺序照常流过
    /// </summary>
    /// <param name="updates">模型的流式更新</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>至多带一份用量的更新流</returns>
    public static async IAsyncEnumerable<ChatResponseUpdate> KeepLast(IAsyncEnumerable<ChatResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageContent? lastUsage = null;
        ChatResponseUpdate? lastCarrier = null; //带用量的那条，补发时沿用它的消息标识，好并进同一条回复
        ExceptionDispatchInfo? failure = null; //流中途抛出：先把已收到的用量交出去再重抛，花掉的不能因失败不记

        // 手动驱动枚举器：C# 不许在带 catch 的 try 里 yield
        IAsyncEnumerator<ChatResponseUpdate> enumerator = updates.GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                }
                catch (Exception e)
                {
                    failure = ExceptionDispatchInfo.Capture(e);
                    break;
                }

                ChatResponseUpdate update = enumerator.Current;
                if (!update.Contents.Any(c => c is UsageContent))
                {
                    yield return update;
                    continue;
                }

                lastUsage = update.Contents.OfType<UsageContent>().Last();
                lastCarrier = update;
                update.Contents = update.Contents.Where(c => c is not UsageContent).ToList();
                if (update.Contents.Count > 0 || update.FinishReason != null) yield return update;
            }
        }

        if (lastUsage != null && lastCarrier != null)
        {
            yield return new ChatResponseUpdate(lastCarrier.Role ?? ChatRole.Assistant, [lastUsage])
            {
                ResponseId = lastCarrier.ResponseId,
                MessageId = lastCarrier.MessageId,
                ModelId = lastCarrier.ModelId,
                CreatedAt = lastCarrier.CreatedAt,
            };
        }

        failure?.Throw();
    }
}
