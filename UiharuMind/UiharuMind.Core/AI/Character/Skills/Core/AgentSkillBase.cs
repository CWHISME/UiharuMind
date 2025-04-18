using Microsoft.SemanticKernel;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Character.Skills;

public abstract class AgentSkillBase
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

    public virtual IAsyncEnumerable<string> DoSkill(ModelRunningData? modelRunningData, string userInput,
        CancellationToken cancellationToken = default)
    {
        // if (modelRunningData is not { IsRunning: true })
        // {
        //     return new AsyncEnumerableWithMessage("Model is not running.");
        // }
        if (SafeCheckModelRunning(ref modelRunningData) == false)
        {
            return new AsyncEnumerableWithMessage("Model is not running.");
        }

        // TrySetParams(LanguageParamsName, LanguageUtils.CurCultureInfo.EnglishName);

        CurModelRunningData = modelRunningData;
        return OnDoSkill(modelRunningData!, userInput, _args, cancellationToken);
    }

    // public abstract CharacterData Character { get; }

    public virtual bool IsConvertableToChatSession => false;

    public virtual ChatSession? TryConvertToChatSession()
    {
        return null;
    }

    protected abstract IAsyncEnumerable<string> OnDoSkill(ModelRunningData modelRunningData, string userInput,
        Dictionary<string, object?>? args,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 如果没有运行中模型，优先取远程模型
    /// </summary>
    /// <param name="modelRunning"></param>
    /// <returns></returns>
    private bool SafeCheckModelRunning(ref ModelRunningData? modelRunning)
    {
        if ((modelRunning == null || IsVision && !modelRunning.IsVisionModel) &&
            LlmManager.Instance.RemoteModelManager.RemoteListModels.Count > 0)
        {
            if (!string.IsNullOrEmpty(LlmManager.Instance.RemoteModelManager.Config.FavoriteModel))
                LlmManager.Instance.RemoteModelManager.RemoteListModels.TryGetValue(
                    LlmManager.Instance.RemoteModelManager.Config.FavoriteModel, out modelRunning);
            foreach (var model in LlmManager.Instance.RemoteModelManager.RemoteListModels)
            {
                if (IsVision && model.Value.IsVisionModel)
                {
                    modelRunning = model.Value;
                    break;
                }

                if (IsVision || model.Value.IsVisionModel) continue;
                // modelRunning = model.Value;
                modelRunning ??= model.Value;
                break;
            }
        }

        if (modelRunning is not { IsRunning: true })
        {
            if (modelRunning?.IsRemoteModel != true)
            {
                return false;
            }

            if (modelRunning.Kernel == null)
            {
                _ = modelRunning.StartLoad(null, null);
            }
        }

        return true;
    }
}