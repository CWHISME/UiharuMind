/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

namespace UiharuMind.Core.Core.Process;

/// <summary>
/// 用于在异步枚举器中出现错误，需要终止，传递错误信息的异步枚举器
/// </summary>
public class AsyncEnumerableWithMessage : IAsyncEnumerable<string>
{
    private readonly string _errorMessage;

    public AsyncEnumerableWithMessage(string errorMessage)
    {
        _errorMessage = errorMessage;
    }

    public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new AsyncEnumeratorWithMessage(_errorMessage);
    }
}

public class AsyncEnumeratorWithMessage : IAsyncEnumerator<string>
{
    private readonly string _errorMessage;
    private bool _hasYielded;

    public AsyncEnumeratorWithMessage(string errorMessage)
    {
        _errorMessage = errorMessage;
        _hasYielded = false;
    }

    public string Current => _errorMessage;

    public ValueTask DisposeAsync() => new ValueTask();

    public ValueTask<bool> MoveNextAsync()
    {
        if (!_hasYielded)
        {
            _hasYielded = true;
            return new ValueTask<bool>(true);
        }

        return new ValueTask<bool>(false);
    }
}