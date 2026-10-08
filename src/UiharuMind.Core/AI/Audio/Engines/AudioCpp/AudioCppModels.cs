using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 一个 audio.cpp 任务（server.json 的 task）
/// </summary>
public enum EAudioCppTask
{
    Tts,
    Asr
}

/// <summary>
/// 把扫描到的本地音频模型变成 server.json 的模型条目
/// </summary>
public static class AudioCppModels
{
    /// <summary>
    /// 任务在 server.json 里的写法
    /// </summary>
    /// <param name="task">任务</param>
    /// <returns>tts / asr</returns>
    public static string ToConfigName(this EAudioCppTask task) => task == EAudioCppTask.Tts ? "tts" : "asr";

    /// <summary>
    /// 扫描结果里的语音合成与语音识别模型；读不出家族的跳过
    /// </summary>
    /// <param name="models">本地模型</param>
    /// <returns>模型条目，id 即本地模型名</returns>
    public static IReadOnlyList<AudioCppModelEntry> FromLocal(IEnumerable<LocalModelEntry> models)
    {
        List<AudioCppModelEntry> entries = [];
        foreach (GGufModelInfo info in models.Select(x => x.Info))
        {
            string? task = info.Kind switch
            {
                ELocalModelKind.SpeechSynthesis => EAudioCppTask.Tts.ToConfigName(),
                ELocalModelKind.SpeechRecognition => EAudioCppTask.Asr.ToConfigName(),
                _ => null
            };
            if (task == null) continue;

            string family = ReadFamily(info.ModelPath);
            if (family.Length == 0) continue;
            entries.Add(new AudioCppModelEntry(info.ModelName, family, ServerPath(info.ModelPath), task));
        }

        return entries;
    }

    /// <summary>
    /// 交给 server 的路径：目录里只有这一个 gguf 时给目录（server 才认得出旁边的音色等伴生文件、列得出音色），
    /// 否则给文件本身，免得同目录多个精度时 server 挑错
    /// </summary>
    /// <param name="modelPath">gguf 文件</param>
    /// <returns>目录或文件</returns>
    public static string ServerPath(string modelPath)
    {
        string? directory = Path.GetDirectoryName(modelPath);
        if (string.IsNullOrEmpty(directory)) return modelPath;
        return Directory.EnumerateFiles(directory, "*.gguf").Take(2).Count() == 1 ? directory : modelPath;
    }

    private static string ReadFamily(string modelPath)
    {
        try
        {
            return AudioCppSpec.ReadFamily(GGufHeaderReader.Read(modelPath));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Warning($"Read audio.cpp model family failed: {modelPath} {e.Message}");
            return "";
        }
    }
}
