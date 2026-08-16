using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.PromptActions;

public abstract class PromptActionBase
{
    private Dictionary<string, object?>? _args;

    public void SetParams(string key, object? value)
    {
        _args ??= new Dictionary<string, object?>();
        _args[key] = value;
    }

    public void RemoveParams(string key)
    {
        _args?.Remove(key);
    }

    public object? GetParam(string key)
    {
        object? value = null;
        _args?.TryGetValue(key, out value);
        return value;
    }

    /// <summary>
    /// 如果指定key不存在则设置参数，否则不做任何操作
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    public void TrySetParams(string key, object? value)
    {
        if (GetParam(key) == null)
            SetParams(key, value);
    }

    // ============================== Common Params ================================

    public void SetLanguage(string text)
    {
        SetParams(CharacterData.ParamsNameLanguage, text);
    }

    public void RemoveLanguage()
    {
        RemoveParams(CharacterData.ParamsNameLanguage);
    }

    //================================================================================

    protected virtual bool IsVision => false;
    protected ModelRunningData? CurModelRunningData;

    public virtual IAsyncEnumerable<string> RunAsync(string userInput,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(LlmManager.Instance.CurrentRunningModel, userInput, cancellationToken);
    }

    public virtual IAsyncEnumerable<string> RunAsync(ModelRunningData? modelRunningData, string userInput,
        CancellationToken cancellationToken = default)
    {
        // if (modelRunningData is not { IsRunning: true })
        // {
        //     return new AsyncEnumerableWithMessage("Model is not running.");
        // }
        if (LlmManager.Instance.TryCheckModelRunning(IsVision, ref modelRunningData) == false)
        {
            return new AsyncEnumerableWithMessage("Model is not running.");
        }

        // TrySetParams(LanguageParamsName, LanguageUtils.CurCultureInfo.EnglishName);

        CurModelRunningData = modelRunningData;
        return OnRunAsync(modelRunningData!, userInput, _args, cancellationToken);
    }

    /// <summary>
    /// 跑一遍并产出<b>完整内容</b>增量（正文之外还有思考等）。默认实现把正文包成
    /// <see cref="TextContent"/>——只有真正走临时会话的形态才有内容流可透出，
    /// 其余形态照旧只有正文，调用方无需分辨。
    /// </summary>
    /// <param name="userInput">用户输入</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>内容增量流</returns>
    public virtual IAsyncEnumerable<AIContent> RunContentAsync(string userInput,
        CancellationToken cancellationToken = default)
    {
        return ToContentStream(RunAsync(userInput, cancellationToken));
    }

    /// <summary>
    /// 把只有正文的流包成内容流
    /// </summary>
    /// <param name="texts">正文增量流</param>
    /// <returns>内容增量流</returns>
    protected static async IAsyncEnumerable<AIContent> ToContentStream(IAsyncEnumerable<string> texts,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (string delta in texts.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return new TextContent(delta);
        }
    }

    // public abstract CharacterData Character { get; }

    public virtual bool IsConvertableToChatSession => false;

    public virtual ChatSession? TryConvertToChatSession()
    {
        return null;
    }

    protected abstract IAsyncEnumerable<string> OnRunAsync(ModelRunningData modelRunningData, string userInput,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default);
}
