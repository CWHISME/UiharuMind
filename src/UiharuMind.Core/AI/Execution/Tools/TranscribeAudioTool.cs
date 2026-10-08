using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 转写工具：把工作区里的音频文件转成文字，可带字幕时间轴（SRT）。用听写的默认识别（本地或在线）；
/// 全文存进草稿目录的 transcripts 下，结果里只回一段摘录，长录音不撑爆上下文
/// </summary>
public static class TranscribeAudioTool
{
    /// <summary>工具名</summary>
    public const string ToolName = "TranscribeAudio";

    /// <summary>草稿目录下放转写结果的子目录</summary>
    public const string TranscriptsFolder = "transcripts";

    private const int ExcerptLength = 4000; //结果里最多回这么多字

    /// <summary>
    /// 创建转写 AIFunction
    /// </summary>
    /// <param name="paths">路径口径（读工作区文件、存到草稿目录）</param>
    /// <returns>工具实例</returns>
    public static AITool Create(AgentPathResolver paths) => Create(paths, AudioFileTranscriber.Shared);

    internal static AITool Create(AgentPathResolver paths, AudioFileTranscriber transcriber)
    {
        return AIFunctionFactory.Create(
            async ([Description("Path of the audio file (wav, mp3 or flac).")]
                    string path,
                    [Description("Also write SRT subtitles with segment timestamps.")]
                    bool subtitles = false,
                    CancellationToken cancellationToken = default) =>
                await TranscribeAsync(paths, transcriber, path, subtitles, cancellationToken).ConfigureAwait(false),
            ToolName,
            "Transcribe speech in an audio file to text, optionally with SRT subtitles. " +
            $"Handles up to {AudioFileTranscriber.MaxDuration.TotalMinutes:0} minutes. " +
            "For video, extract the audio first (e.g. ffmpeg -i in.mp4 -vn -ac 1 out.wav).");
    }

    private static async Task<string> TranscribeAsync(AgentPathResolver paths, AudioFileTranscriber transcriber,
        string path, bool subtitles, CancellationToken cancellationToken)
    {
        if (!paths.TryResolve(path, out string full) || !File.Exists(full)) return $"Error: file not found: {path}";
        string extension = Path.GetExtension(full).TrimStart('.').ToLowerInvariant();
        if (!SoundFlowAudioFile.Extensions.Contains(extension))
            return $"Error: '{Path.GetFileName(full)}' is not wav, mp3 or flac. Convert it first, " +
                   "e.g. ffmpeg -i input -vn -ac 1 output.wav";

        Transcript transcript;
        try
        {
            transcript = await transcriber.TranscribeAsync(full, subtitles, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return $"Error: transcription failed: {e.Message}\nTell the user to check the speech recognition settings.";
        }

        if (transcript.Segments.Count == 0) return "No speech was recognized in this file.";

        string directory = AgentOutputLayout.EnsureDraftSubdirectory(paths.DraftRoot, TranscriptsFolder);
        string stem = Path.GetFileNameWithoutExtension(full);
        string text = transcript.Text;
        string textFile = Unique(directory, stem, ".txt");
        await File.WriteAllTextAsync(textFile, text, cancellationToken).ConfigureAwait(false);
        StringBuilder result = new($"Transcribed {transcript.Duration.TotalSeconds:0} s of audio.\n");
        if (transcript.Truncated)
            result.Append($"Only the first {AudioFileTranscriber.MaxDuration.TotalMinutes:0} minutes were transcribed.\n");
        result.Append("Text: ").Append(paths.ToPortable(textFile)).Append('\n');
        if (subtitles)
        {
            string srtFile = Unique(directory, stem, ".srt");
            await File.WriteAllTextAsync(srtFile, ToSrt(transcript.Segments), cancellationToken).ConfigureAwait(false);
            result.Append("Subtitles: ").Append(paths.ToPortable(srtFile)).Append('\n');
        }

        result.Append('\n').Append(text.Length <= ExcerptLength ? text : text[..ExcerptLength] + "\n[... see the text file for the rest]");
        return result.ToString();
    }

    /// <summary>
    /// 排成 SRT
    /// </summary>
    /// <param name="segments">各段</param>
    /// <returns>SRT 正文</returns>
    public static string ToSrt(IReadOnlyList<TranscriptSegment> segments)
    {
        StringBuilder srt = new();
        for (int i = 0; i < segments.Count; i++)
        {
            srt.Append(i + 1).Append('\n')
                .Append(Stamp(segments[i].Start)).Append(" --> ").Append(Stamp(segments[i].End)).Append('\n')
                .Append(segments[i].Text).Append("\n\n");
        }

        return srt.ToString();
    }

    private static string Stamp(TimeSpan time) =>
        $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";

    private static string Unique(string directory, string stem, string extension)
    {
        string path = Path.Combine(directory, stem + extension);
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(directory, $"{stem}-{i}{extension}");
        return path;
    }
}
