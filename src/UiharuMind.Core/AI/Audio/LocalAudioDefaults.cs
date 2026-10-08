namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 朗读 / 听写默认用本机哪个模型。本地模型与在线服务二选一：设了本地的就把路由切回本地引擎；
/// 默认是在线服务时，没有哪个本地模型算「默认」。「获取模型」与「语音模型」页签共用这一份口径
/// </summary>
/// <param name="engine">语音引擎</param>
/// <param name="router">朗读、听写各用哪个</param>
public sealed class LocalAudioDefaults(AudioCppEngine engine, AudioRouter router)
{
    /// <summary>
    /// 默认改用本机这个模型
    /// </summary>
    /// <param name="task">任务</param>
    /// <param name="modelName">模型名</param>
    public void Use(EAudioCppTask task, string modelName)
    {
        if (task == EAudioCppTask.Tts) engine.Settings.TtsModel = modelName;
        else engine.Settings.AsrModel = modelName;
        engine.Settings.Save();
        router.UseLocal(ToCapability(task));
    }

    /// <summary>
    /// 实际默认用的本地模型：设置里选的，没选或不在了就第一个
    /// </summary>
    /// <param name="task">任务</param>
    /// <returns>模型名；默认是在线服务、或本机一个都没有为 null</returns>
    public string? Resolve(EAudioCppTask task)
    {
        if (router.DefaultService(ToCapability(task)) != null) return null;
        try
        {
            return engine.ResolveModel(task).Id;
        }
        catch (AudioModelMissingException)
        {
            return null;
        }
    }

    private static EAudioCapability ToCapability(EAudioCppTask task) =>
        task == EAudioCppTask.Tts ? EAudioCapability.Tts : EAudioCapability.Asr;
}
