using Microsoft.SemanticKernel;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.Skills;

public abstract class AgentSkill
{
    private Dictionary<string, object?>? _args;

    public void SetParams(string key, object? value)
    {
        _args ??= new Dictionary<string, object?>();
        _args[key] = value;
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
    private void TrySetParams(string key, object? value)
    {
        if (GetParam(key) == null)
            SetParams(key, value);
    }

    // ============================== Common Params ================================

    public void SetLangate(string text)
    {
        SetParams(CharacterData.ParamsNameLanguage, text);
    }

    //================================================================================

    public IAsyncEnumerable<string> DoSkill(ModelRunningData? modelRunningData, string userInput,
        CancellationToken cancellationToken = default)
    {
        if (modelRunningData is not { IsRunning: true })
        {
            return new AsyncEnumerableWithMessage("Model is not running.");
        }

        // TrySetParams(LanguageParamsName, LanguageUtils.CurCultureInfo.EnglishName);

        return OnDoSkill(modelRunningData, userInput, _args, cancellationToken);
    }

    protected abstract IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string userInput,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default);
}