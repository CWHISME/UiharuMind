namespace UiharuMind.Core.AI.Net.Errors;

internal static class ExceptionChain
{
    /// <summary>在异常的内部链及 AggregateException 中查找指定类型</summary>
    /// <param name="error">顶层异常</param>
    /// <returns>找到时返回第一个实例，否则返回 null</returns>
    public static TException? Find<TException>(Exception? error) where TException : Exception
    {
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            if (current is TException found) return found;
            if (current is not AggregateException aggregate) continue;

            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
            {
                if (Find<TException>(inner) is { } nested) return nested;
            }
        }

        return null;
    }
}
